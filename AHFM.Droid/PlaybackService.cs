using System;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Android.Util;

namespace AHFM.Droid {
  [Service]
  public class PlaybackService : Service {
    const string StreamAddress = "http://us.ah.fm:443";

    const string CancelPlaybackAction = "CancelPlayback";

    public delegate void PlaybackStatusChangedHandler(object source, PlaybackStatusChangedEventArgs e);
    public event PlaybackStatusChangedHandler PlaybackStatusChanged;

    Status _backendStatus;
    public Status BackendStatus {
      get {
        return _backendStatus;
      }
      private set {
        var oldValue = _backendStatus;
        _backendStatus = value;

        Log.Info(nameof(PlaybackService), "BackendStatusChanged: " + BackendStatus.ToString());
        UpdateNotificationDisplay();

        TriggerPlaybackStatusChanged(oldValue, _backendStatus);
      }
    }

    private void TriggerPlaybackStatusChanged(Status oldStatus, Status newStatus) {
      if (PlaybackStatusChanged != null) {
        PlaybackStatusChanged(this, new PlaybackStatusChangedEventArgs(oldStatus, newStatus));
      }
    }

    bool _foregroundStatus;
    public bool ActivityInForeground {
      get {
        return _foregroundStatus;
      }
      internal set {
        _foregroundStatus = value;
        UpdateNotificationDisplay();
      }
    }

    MediaPlayer mediaPlayer;
    MediaSession mediaSession;
    
    NotificationManager notificationManager;

    public override void OnCreate() {
      base.OnCreate();
      mediaPlayer = new MediaPlayer();
      mediaPlayer.SetAudioStreamType(Stream.Music);
      mediaPlayer.SetDataSource(StreamAddress);

      mediaPlayer.Prepared += MediaPlayerPrepared;

      mediaSession = new MediaSession(this, PackageName);

      notificationManager = (NotificationManager)GetSystemService(NotificationService);
    }
    
    [Obsolete] // Xamarin bug?
    public override StartCommandResult OnStartCommand(Intent intent, StartCommandFlags flags, int startId) {
      if (intent == null) {
        return StartCommandResult.NotSticky;
      }

      switch(intent.Action) {
        case CancelPlaybackAction:
          Stop();
          break;
        default:
          break;
      }

      return StartCommandResult.NotSticky;
    }

    public void Prepare() {
      if (BackendStatus != Status.Idle && BackendStatus != Status.Stopped) {
        throw new InvalidOperationException("Cannot prepare from the current state: " + BackendStatus.ToString());
      }

      mediaPlayer.PrepareAsync();
      BackendStatus = Status.Preparing;
    }

    public void Start() {
      if (BackendStatus != Status.Prepared) {
        throw new InvalidOperationException("Cannot start until prepared");
      }

      mediaPlayer.Start();
      BackendStatus = Status.Started;
    }

    public void Stop() {
      if (BackendStatus != Status.Started) {
        throw new InvalidOperationException("Cannot stop until started");
      }

      mediaPlayer.Stop();
      Reset();
      BackendStatus = Status.Stopped;
    }

    private void Reset() {
      mediaPlayer.Reset();
      mediaPlayer.SetDataSource(StreamAddress);
    }

    private void MediaPlayerPrepared(object sender, EventArgs e) {
      BackendStatus = Status.Prepared;
    }

    private void UpdateNotificationDisplay() {
      if (ActivityInForeground) {
        notificationManager.Cancel(0);
        mediaSession.Active = false;
        return;
      }

      var newNotification = BuildNotification();
      mediaSession.Active = true;
      notificationManager.Notify(0, newNotification);
    }

    Notification BuildNotification() {
      var activityIntent = new Intent(this, typeof(MainActivity));
      var stackBuilder = TaskStackBuilder.Create(this);

      stackBuilder.AddParentStack(Java.Lang.Class.FromType(typeof(MainActivity)));
      stackBuilder.AddNextIntent(activityIntent);

      var notificationIntent = stackBuilder.GetPendingIntent(0, PendingIntentFlags.UpdateCurrent);

      var dismissalIntent = new Intent(this, Java.Lang.Class.FromType(GetType()));
      dismissalIntent.SetAction(CancelPlaybackAction);
      var dismissalPIntent = PendingIntent.GetService(this, 0, dismissalIntent, PendingIntentFlags.UpdateCurrent);

      var notificationPic = BitmapFactory.DecodeResource(Resources, Resource.Drawable.NotificationPicture);
      var mediaStyle = new Notification.MediaStyle().SetMediaSession(mediaSession.SessionToken);

      return new Notification.Builder(this)
        .SetSmallIcon(Resource.Drawable.NotificationIcon)
        .SetContentTitle(GetString(Resource.String.ApplicationName))
        .SetSubText(GetString(Resource.String.NotificationText))
        .SetLargeIcon(notificationPic)
        .SetStyle(mediaStyle)
        .SetContentIntent(notificationIntent)
        .SetDeleteIntent(dismissalPIntent)
        .SetOngoing(BackendStatus == Status.Started)
        .Build();
    }

    public enum Status {
      Idle,
      Preparing,
      Prepared,
      Started,
      Stopped,
      Destroyed
    }

    public override void OnDestroy() {
      base.OnDestroy();

      Dispose(false);
    }

    protected override void Dispose(bool disposing) {
      base.Dispose(disposing);

      if (mediaPlayer != null) {
        mediaPlayer.Release();
        mediaPlayer.Dispose();
        mediaPlayer = null;
      }

      if (mediaSession != null) {
        mediaSession.Release();
        mediaSession.Dispose();
        mediaSession = null;
      }
    }

    public override IBinder OnBind(Intent intent) {
      return new PlaybackBinder(this);
    }
  }

  public class PlaybackBinder : Binder {
    readonly PlaybackService service;

    public delegate void PlaybackStatusChangedHandler(object source, PlaybackStatusChangedEventArgs e);
    public event PlaybackStatusChangedHandler PlaybackStatusChanged;

    public PlaybackBinder(PlaybackService service) {
      if (service == null) {
        throw new ArgumentNullException(nameof(service));
      }

      this.service = service;
      this.service.PlaybackStatusChanged += ServicePlaybackStatusChanged;
    }

    private void ServicePlaybackStatusChanged(object source, PlaybackStatusChangedEventArgs e) {
      if (PlaybackStatusChanged != null) {
        PlaybackStatusChanged(source, e);
      }
    }

    public PlaybackService.Status ServiceStatus {
      get {
        return service.BackendStatus;
      }
    }

    public void Prepare() {
      service.Prepare();
    }

    public void Start() {
      service.Start();
    }

    public void Stop() {
      service.Stop();
    }

    public void SetActivityInForeground(bool isForeground) {
      service.ActivityInForeground = isForeground;
    }
  }

  public class PlaybackStatusChangedEventArgs : EventArgs {
    public PlaybackService.Status OldStatus { get; private set; }
    public PlaybackService.Status NewStatus { get; private set; }

    public PlaybackStatusChangedEventArgs(PlaybackService.Status oldStatus, PlaybackService.Status newStatus) {
      OldStatus = oldStatus;
      NewStatus = newStatus;
    }
  }
}