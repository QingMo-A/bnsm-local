/// <summary>Desktop HTTP adapter for the shared local debug actions.</summary>
internal static class DebugApi
{
    public static void Map(WebApplication app, LocalRuntime runtime)
    {
        app.MapGet("/diagnostics",()=>Results.Content(DiagnosticsHtml,"text/html; charset=utf-8"));
        app.MapGet("/debug/logs",()=>Results.Json(new{ok=Bnsm.Server.ServerDiagnostics.Current!=null,text=Bnsm.Server.ServerDiagnostics.Current?.Tail()??"当前进程尚未启用日志。",activeGameConnections=runtime.ActiveGameConnections,lastGameRequestUtc=runtime.LastGameRequestUtc}));
        app.MapGet("/debug/logs/export",()=>Bnsm.Server.ServerDiagnostics.Current is { } logs?Results.File(logs.Export(),"application/zip","bnsm-logs-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+".zip"):Results.NotFound());
        var service = new LocalDebugService(runtime);
        app.MapGet("/debug/bots",()=>Results.Json(service.Handle("GET","/debug/bots",null,null)));
        app.MapGet("/debug/bots/opponents",(uint? uid)=>Results.Json(service.Handle("GET","/debug/bots/opponents",uid,null)));
        foreach(string route in new[]{"/debug/bots/generate","/debug/bots/configure"})
        {
            string path=route;
            app.MapPost(path,async(HttpContext ctx)=>{using var reader=new StreamReader(ctx.Request.Body);return Results.Json(service.Handle("POST",path,null,await reader.ReadToEndAsync()));});
        }
        app.MapGet("/debug/players", (uint? uid) => Results.Json(service.Handle("GET","/debug/players",uid,null)));
        app.MapGet("/debug/state", (uint? uid) => Results.Json(service.Handle("GET","/debug/state",uid,null)));
        app.MapPost("/debug/options", async (HttpContext ctx) => {
            using var reader = new StreamReader(ctx.Request.Body);
            return Results.Json(service.Handle("POST","/debug/options",null,await reader.ReadToEndAsync()));
        });
        app.MapPost("/debug/challenges/restore", async (HttpContext ctx) => {
            using var reader = new StreamReader(ctx.Request.Body);
            return Results.Json(service.Handle("POST","/debug/challenges/restore",null,await reader.ReadToEndAsync()));
        });
        app.MapPost("/debug/level", async (HttpContext ctx) => {
            using var reader = new StreamReader(ctx.Request.Body);
            return Results.Json(service.Handle("POST","/debug/level",null,await reader.ReadToEndAsync()));
        });
        app.MapPost("/debug/card-exp", async (HttpContext ctx) => {
            using var reader = new StreamReader(ctx.Request.Body);
            return Results.Json(service.Handle("POST","/debug/card-exp",null,await reader.ReadToEndAsync()));
        });
        app.MapPost("/debug/vip", async (HttpContext ctx) => {
            using var reader = new StreamReader(ctx.Request.Body);
            return Results.Json(service.Handle("POST","/debug/vip",null,await reader.ReadToEndAsync()));
        });
        app.MapPost("/debug/currency", async (HttpContext ctx) => {
            using var reader = new StreamReader(ctx.Request.Body);
            return Results.Json(service.Handle("POST","/debug/currency",null,await reader.ReadToEndAsync()));
        });
        app.MapPost("/debug/skill-point", async (HttpContext ctx) => {
            using var reader = new StreamReader(ctx.Request.Body);
            return Results.Json(service.Handle("POST","/debug/skill-point",null,await reader.ReadToEndAsync()));
        });
    }
    private const string DiagnosticsHtml="""
<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>剑灵本地服务端 · 日志</title>
<style>body{margin:0;background:#151b23;color:#e5edf5;font:16px system-ui}main{max-width:1100px;margin:32px auto;padding:0 20px}h1{font-size:24px}button,a{display:inline-block;background:#2f89a5;color:white;border:0;border-radius:7px;padding:10px 18px;margin:0 10px 12px 0;text-decoration:none;cursor:pointer}pre{background:#0d1219;border:1px solid #344050;padding:16px;border-radius:8px;white-space:pre-wrap;word-break:break-word;max-height:72vh;overflow:auto;font:13px/1.6 ui-monospace,monospace}p{color:#a7b6c5}</style>
<main><h1>服务端日志</h1><p>最近记录 · UTC 时间 · 日志最多保留约 8 MiB。导出包含历史日志和运行信息，不包含存档。</p><button id="refresh">刷新日志</button><a href="/debug/logs/export">导出日志 ZIP</a><span id="state"></span><pre id="log">读取中…</pre></main>
<script>async function refresh(){try{let r=await fetch('/debug/logs',{cache:'no-store'});if(!r.ok)throw Error('HTTP '+r.status);let d=await r.json();document.getElementById('log').textContent=d.text;document.getElementById('state').textContent='游戏连接：'+d.activeGameConnections;}catch(e){document.getElementById('state').textContent='读取失败：'+e.message;}}document.getElementById('refresh').onclick=refresh;refresh();</script></html>
""";
}
