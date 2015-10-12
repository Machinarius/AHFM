using System;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;

namespace AHFM.Droid {
  [Activity(Label = "AHFM", MainLauncher = true, Icon = "@drawable/NotificationIcon")]
  public class MainActivity : Activity, IServiceConnection {
    TextView serviceStatusText;
    Button playbackButton;

    PlaybackBinder playbackBinder;

    bool _resumed;
    bool resumed {
      get {
        return _resumed;
      }
      set {
        _resumed = value;
        UpdateServiceForegroundStatus();
      }
    }

    protected override void OnCreate(Bundle savedInstanceState) {
      base.OnCreate(savedInstanceState);
      SetContentView(Resource.Layout.Main);

      serviceStatusText = FindViewById<TextView>(Resource.Id.ServiceStatusText);
      playbackButton = FindViewById<Button>(Resource.Id.PlaybackButton);
      playbackButton.Click += PlaybackButtonClick;

      var serviceIntent = new Intent(this, typeof(PlaybackService));
      StartService(serviceIntent);
      BindService(serviceIntent, this, Bind.Important);
    }

    protected override void OnDestroy() {
      base.OnDestroy();

      UnbindService(this);
    }

    protected override void OnResume() {
      base.OnResume();

      resumed = true;
    }

    protected override void OnPause() {
      base.OnPause();

      resumed = false;
    }

    private void UpdateServiceForegroundStatus() {
      if (playbackBinder != null) {
        playbackBinder.SetActivityInForeground(resumed);
      }
    }

    private void PlaybackButtonClick(object sender, EventArgs e) {
      if (playbackBinder == null) {
        throw new InvalidOperationException("Cannot control playback until connected to the playback service");
      }

      switch(playbackBinder.ServiceStatus) {
        case PlaybackService.Status.Idle:
        case PlaybackService.Status.Stopped:
          playbackBinder.Prepare();
          break;
        case PlaybackService.Status.Started:
          playbackBinder.Stop();
          break;
        default: break;
      }
    }

    public void OnServiceConnected(ComponentName name, IBinder service) {
      playbackBinder = service as PlaybackBinder;
      if (playbackBinder == null) {
        throw new InvalidOperationException("Cannot bind to any service other than " + nameof(PlaybackService));
      }

      OnPlaybackServiceBound();
    }

    private void OnPlaybackServiceBound() {
      playbackBinder.PlaybackStatusChanged += PlaybackStatusChanged;
      UpdateUIToReflectServiceStatus();
      UpdateServiceForegroundStatus();
    }

    private void UpdateUIToReflectServiceStatus() {
      switch (playbackBinder.ServiceStatus) {
        case PlaybackService.Status.Preparing:
          serviceStatusText.Text = GetString(Resource.String.Preparing);
          playbackButton.Text = GetString(Resource.String.Play);
          playbackButton.Enabled = false;
          break;
        case PlaybackService.Status.Started:
          serviceStatusText.Text = GetString(Resource.String.Started);
          playbackButton.Text = GetString(Resource.String.Stop);
          playbackButton.Enabled = true;
          break;
        default:
          serviceStatusText.Text = GetString(Resource.String.Stopped);
          playbackButton.Text = GetString(Resource.String.Play);
          playbackButton.Enabled = true;
          break;
      }
    }

    private void PlaybackStatusChanged(object source, PlaybackStatusChangedEventArgs e) {
      UpdateUIToReflectServiceStatus();

      // TODO: Migrate this logic to the service itself?
      if (e.OldStatus == PlaybackService.Status.Preparing && e.NewStatus == PlaybackService.Status.Prepared) {
        playbackBinder.Start();
      }
    }

    public void OnServiceDisconnected(ComponentName name) {
      playbackBinder.PlaybackStatusChanged -= PlaybackStatusChanged;
      playbackBinder = null;
    }
  }
}

