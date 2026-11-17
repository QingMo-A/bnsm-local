package local.bnsm.bridge;

import android.app.*;
import android.content.*;
import android.os.*;

/** Same process as Unity. A killed process is restored through the game's normal cold boot. */
public final class EmbeddedService extends Service {
    public static void start(Activity activity) {
        activity.runOnUiThread(() -> {
            try { activity.startForegroundService(new Intent(activity, EmbeddedService.class)); }
            catch (Exception error) { LocalSupport.notice("前台保活启动失败："+error.getClass().getSimpleName()); }
        });
    }
    public static void stop(Activity activity) { activity.runOnUiThread(()->activity.stopService(new Intent(activity,EmbeddedService.class))); }
    public void onCreate() {
        super.onCreate();
        NotificationManager manager=(NotificationManager)getSystemService(NOTIFICATION_SERVICE);
        manager.createNotificationChannel(new NotificationChannel("embedded-game", "游戏内置服务端", NotificationManager.IMPORTANCE_LOW));
        Intent open=getPackageManager().getLaunchIntentForPackage(getPackageName());
        PendingIntent pending=PendingIntent.getActivity(this, 0, open, PendingIntent.FLAG_IMMUTABLE|PendingIntent.FLAG_UPDATE_CURRENT);
        startForeground(19701,new Notification.Builder(this,"embedded-game")
            .setContentTitle("战斗吧剑灵 · 内置服务端")
            .setContentText("自动保存已启用；点猫咪图标可保存进度或导出存档与日志")
            .setSmallIcon(android.R.drawable.ic_dialog_info).setContentIntent(pending).setOngoing(true).build());
    }
    public int onStartCommand(Intent intent,int flags,int id) { return START_NOT_STICKY; }
    public void onTaskRemoved(Intent rootIntent){SaveBridge.taskRemoved(this);super.onTaskRemoved(rootIntent);}
    public IBinder onBind(Intent intent) { return null; }
}
