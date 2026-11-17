using Android.App;
using Android.Content;
using Android.OS;
using Bnsm.Server;

namespace Bnsm.AndroidHost;

/// <summary>Priority-only binding. Exposes no save, command or network operations.</summary>
[Service(Exported=true,Permission="local.bnsm.serverhost.permission.BIND_GAME")]
[Android.Runtime.Register("local/bnsm/serverhost/GameBindingService")]
public sealed class GameBindingService:Service
{
    private readonly Binder binder=new();
    public static bool IsGameBound {get;private set;}
    public override IBinder? OnBind(Intent? intent)
    {
        if(intent?.Action!="local.bnsm.serverhost.BIND_GAME")return null;
        IsGameBound=true;ServerDiagnostics.Info("Visible game bound to companion process");ServerService.RecoverWhenBound(this);return binder;
    }
    public override bool OnUnbind(Intent? intent){IsGameBound=false;ServerDiagnostics.Info("Game released companion binding");return true;}
    public override void OnRebind(Intent? intent){base.OnRebind(intent);IsGameBound=true;ServerDiagnostics.Info("Visible game rebound to companion process");ServerService.RecoverWhenBound(this);}
    public override void OnDestroy(){IsGameBound=false;binder.Dispose();base.OnDestroy();}
}
