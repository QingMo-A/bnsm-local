package local.bnsm.bridge;

import android.content.Context;
import org.json.JSONObject;
import java.io.IOException;

/** File operations shared by the legacy provider and the foreground gateway. */
public final class ManagementActions {
    public static JSONObject execute(Context context,String method,String argument,String name)throws Exception {
        SharedWorkspace files=new SharedWorkspace(context.getFilesDir(),context.getExternalFilesDir(null));
        if(method.equals("list"))return files.list();
        if(method.equals("new")||method.equals("switch")||method.equals("rename"))return files.change(method,argument,name);
        if(method.equals("delete")){
            if(!"active".equals(name)&&!"inactive".equals(name))throw new IOException("删除确认无效，请重新选择存档");
            return files.deleteProfile(argument,"active".equals(name));
        }
        if(method.equals("export"))return new JSONObject().put("file",files.exportProfile(argument).getName());
        if(method.equals("log"))return new JSONObject().put("text",files.viewLog(argument));
        if(method.equals("exportLog"))return new JSONObject().put("file",files.exportLog(argument).getName());
        if(method.equals("exportLogs"))return new JSONObject().put("file",files.bundle(null,"").getName());
        if(method.equals("file"))return new JSONObject().put("file",files.exported(argument).getName());
        if(method.equals("bots"))return files.bots();
        if(method.equals("botGenerate")||method.equals("botConfigure"))return files.queueBots(method,argument);
        throw new IOException("不支持的存档操作，请更新游戏和存档管家");
    }
    private ManagementActions(){}
}
