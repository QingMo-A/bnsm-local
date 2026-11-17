using Android.App;
using Android.Content;
using Android.Widget;
using System.Text.Json;

namespace Bnsm.AndroidHost;

public sealed partial class MainActivity
{
    private sealed record ProfileRow(string Id,string Name,bool Active);
    private void OpenSaveManager()
    {
        if(ServerService.Startup!=null) {
            var dialog=new AlertDialog.Builder(this);dialog.SetTitle("打开存档管理");
            dialog.SetMessage("会先保存并停止服务端，游戏连接将中断。请结束当前游戏操作后继续。");
            dialog.SetNegativeButton("取消",(_,_)=>{});dialog.SetPositiveButton("保存并继续",async(_,_)=>await Work(async()=>{await ServerService.StopRequested(this,"save-manager");await ShowSaveManager();}));dialog.Show();
        } else _=Work(ShowSaveManager);
    }
    private async Task ShowSaveManager()
    {
        var manager=ServerService.SaveManager(this);
        var panel=new LinearLayout(this){Orientation=Orientation.Vertical};var rows=new List<ProfileRow>();
        var info=new TextView(this){TextSize=14,Text="原手机档已保留；当前使用独立管理的存档。操作完成后关闭本页，点击启动服务端。"};panel.AddView(info);
        var picker=new Spinner(this);panel.AddView(picker);
        ProfileRow Selected()=>rows.Count>0&&picker.SelectedItemPosition>=0?rows[picker.SelectedItemPosition]:throw new InvalidOperationException("请选择存档");
        void Refresh()
        {
            string? selected=picker.SelectedItemPosition>=0&&picker.SelectedItemPosition<rows.Count?rows[picker.SelectedItemPosition].Id:null;
            JsonElement snapshot=manager.Execute("list");rows.Clear();
            foreach(var row in snapshot.GetProperty("profiles").EnumerateArray())rows.Add(new(row.GetProperty("Id").GetString()!,row.GetProperty("Name").GetString()!,row.GetProperty("active").GetBoolean()));
            var adapter=new ArrayAdapter<string>(this,Android.Resource.Layout.SimpleSpinnerItem,rows.Select(r=>(r.Active?"当前 · ":"")+r.Name).ToArray());adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);picker.Adapter=adapter;
            int at=rows.FindIndex(r=>r.Id==selected);if(at<0)at=rows.FindIndex(r=>r.Active);picker.SetSelection(Math.Max(0,at));
        }
        async Task Command(string command,string? name=null)
        {
            var row=Selected();await Task.Run(()=>manager.Execute(command,id:row.Id,name:name));Refresh();Show("存档操作已完成："+command+" · "+row.Name);
        }
        void Buttons(string a,Action first,string b,Action second)
        {
            var line=new LinearLayout(this){Orientation=Orientation.Horizontal};
            var left=new Button(this){Text=a};var right=new Button(this){Text=b};
            line.AddView(left,new LinearLayout.LayoutParams(0,-2,1));line.AddView(right,new LinearLayout.LayoutParams(0,-2,1));panel.AddView(line);
            left.Click+=(_,_)=>first();right.Click+=(_,_)=>second();
        }
        Buttons("新建存档",()=>AskText("新建并切换存档","新的冒险",async name=>{await Task.Run(()=>manager.Execute("new",name:name));Refresh();Show("已新建并切换存档；原存档保留。");}),
            "重命名",()=>AskText("重命名存档",Selected().Name,name=>Command("rename",name)));
        Buttons("切换到所选存档",()=>_=Work(()=>Command("switch")),"备份所选存档",()=>_=Work(()=>Command("backup")));
        Buttons("导出所选存档",()=>_=Work(async()=>{
            var row=Selected();string temporary=NewExportPath("save");await Task.Run(()=>manager.Execute("export",id:row.Id,file:temporary));ChooseExport(temporary,"bnsm-save-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".bnsm-save.zip");
        }),"导入备份为新存档",()=>{
            var intent=new Intent(Intent.ActionOpenDocument);intent.AddCategory(Intent.CategoryOpenable);intent.SetType("*/*");StartActivityForResult(intent,ImportSave);
        });
        Buttons("从备份恢复",()=>{
            var row=Selected();var backups=manager.Backups(row.Id);
            if(backups.Count==0){Show("所选存档还没有备份。");return;}
            var pickerDialog=new AlertDialog.Builder(this);pickerDialog.SetTitle("选择 "+row.Name+" 的备份");pickerDialog.SetItems(backups.Select(b=>b.Name).ToArray(),(_,e)=>{
                var backup=backups[e.Which];var confirm=new AlertDialog.Builder(this);confirm.SetTitle("恢复存档？");confirm.SetMessage("将覆盖所选存档“"+row.Name+"”。恢复前会自动备份当前进度。");
                confirm.SetNegativeButton("取消",(_,_)=>{});confirm.SetPositiveButton("备份并恢复",async(_,_)=>await Work(async()=>{await Task.Run(()=>manager.Execute("restore",id:row.Id,file:backup.Path));Refresh();Show("已恢复所选备份，恢复前进度也已备份。");}));confirm.Show();
            });pickerDialog.Show();
        },"归档所选存档",()=>{
            var row=Selected();if(row.Active){Show("请先切换到其他存档，再归档当前存档。");return;}
            var confirm=new AlertDialog.Builder(this);confirm.SetTitle("归档存档？");confirm.SetMessage("“"+row.Name+"”将保存备份并移出列表，可从全部备份中重新导入。");
            confirm.SetNegativeButton("取消",(_,_)=>{});confirm.SetPositiveButton("归档",async(_,_)=>await Work(()=>Command("archive")));confirm.Show();
        });
        Buttons("全部备份 · 恢复为新存档",()=>_=Work(async()=>{
            var backups=await Task.Run(()=>manager.AllBackups());
            if(backups.Count==0){Show("暂无备份。");return;}
            var chooser=new AlertDialog.Builder(this);chooser.SetTitle("恢复备份为新存档 · 含归档进度");
            chooser.SetItems(backups.Select(b=>b.Name).ToArray(),async(_,e)=>await Work(async()=>{
                await Task.Run(()=>manager.Execute("import",name:"备份恢复 "+DateTime.Now.ToString("MM-dd HH:mm"),file:backups[e.Which].Path));Refresh();Show("备份已恢复为新存档；当前存档保持，请选择后切换。");
            }));chooser.Show();
        }),"导出最近备份",()=>_=Work(async()=>{
            var row=Selected();var backups=manager.Backups(row.Id);if(backups.Count==0){Show("所选存档还没有备份。");return;}
            string path=NewExportPath("backup");await Task.Run(()=>File.Copy(backups[0].Path,path));ChooseExport(path,backups[0].Name);
        }));
        var scroll=new ScrollView(this);scroll.AddView(panel);
        await Task.Run(()=>manager.Execute("list"));Refresh();refreshProfiles=Refresh;
        var builder=new AlertDialog.Builder(this);builder.SetTitle("存档管理");builder.SetView(scroll);builder.SetPositiveButton("关闭",(_,_)=>{});var dialog=builder.Create()!;
        dialog.DismissEvent+=(_,_)=>refreshProfiles=null;dialog.Show();
    }
}
