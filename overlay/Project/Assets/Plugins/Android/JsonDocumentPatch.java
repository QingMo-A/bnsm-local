package local.bnsm.bridge;

import java.io.IOException;
import org.json.JSONTokener;

/** Replace one root property without round-tripping opaque save numbers through Android's Double parser. */
public final class JsonDocumentPatch {
    private final String text;
    private int at;
    private JsonDocumentPatch(String text){this.text=text;}
    private IOException invalid(){return new IOException("人机存档 JSON 结构无效，原文件未修改");}
    private void ws(){while(at<text.length()&&" \r\n\t".indexOf(text.charAt(at))>=0)at++;}
    private boolean take(char c){ws();if(at<text.length()&&text.charAt(at)==c){at++;return true;}return false;}
    private void need(char c)throws IOException {if(!take(c))throw invalid();}
    private int scanString()throws IOException {
        ws();int start=at;need('"');
        while(at<text.length()){
            char c=text.charAt(at++);
            if(c=='"')return start;
            if(c<32)throw invalid();
            if(c=='\\'){
                if(at==text.length())throw invalid();char escaped=text.charAt(at++);
                if(escaped=='u'){for(int i=0;i<4;i++)if(at==text.length()||Character.digit(text.charAt(at++),16)<0)throw invalid();}
                else if("\"\\/bfnrt".indexOf(escaped)<0)throw invalid();
            }
        }
        throw invalid();
    }
    private String string()throws Exception {
        int start=scanString();return (String)new JSONTokener(text.substring(start,at)).nextValue();
    }
    private boolean digit(){return at<text.length()&&text.charAt(at)>='0'&&text.charAt(at)<='9';}
    private void number()throws IOException {
        if(text.charAt(at)=='-')at++;
        if(!digit())throw invalid();
        if(text.charAt(at)=='0'){at++;if(digit())throw invalid();}
        else while(digit())at++;
        if(at<text.length()&&text.charAt(at)=='.'){at++;if(!digit())throw invalid();while(digit())at++;}
        if(at<text.length()&&(text.charAt(at)=='e'||text.charAt(at)=='E')){
            at++;if(at<text.length()&&(text.charAt(at)=='+'||text.charAt(at)=='-'))at++;
            if(!digit())throw invalid();while(digit())at++;
        }
    }
    private void value(int depth)throws Exception {
        if(depth>128)throw invalid();ws();if(at==text.length())throw invalid();char c=text.charAt(at);
        // Only replace() needs decoded root keys. Validation scans opaque
        // strings/numbers directly, without per-field tokenizers, matchers or
        // numeric conversions (including the native UInt64 sentinel).
        if(c=='"'){scanString();return;}
        if(c=='{'){
            at++;if(take('}'))return;
            do{scanString();need(':');value(depth+1);}while(take(','));need('}');return;
        }
        if(c=='['){at++;if(take(']'))return;do{value(depth+1);}while(take(','));need(']');return;}
        if(text.startsWith("true",at)){at+=4;return;}
        if(text.startsWith("false",at)){at+=5;return;}
        if(text.startsWith("null",at)){at+=4;return;}
        number();
    }
    private void end()throws IOException {ws();if(at!=text.length())throw invalid();}
    public static void validate(String document)throws Exception {
        JsonDocumentPatch input=new JsonDocumentPatch(document);input.value(0);input.end();
    }
    public static String replace(String document,String key,String replacement)throws Exception {
        JsonDocumentPatch input=new JsonDocumentPatch(document),next=new JsonDocumentPatch(replacement);
        next.value(0);next.end();input.need('{');int start=-1,end=-1,count=0;
        input.ws();if(!input.take('}')){
            do{
                String name=input.string();input.need(':');input.ws();int from=input.at;input.value(1);count++;
                if(name.equals(key)){if(start>=0)throw input.invalid();start=from;end=input.at;}
            }while(input.take(','));input.need('}');
        }
        int close=input.at-1;input.end();
        if(start>=0)return document.substring(0,start)+replacement+document.substring(end);
        return document.substring(0,close)+(count==0?"":",")+org.json.JSONObject.quote(key)+":"+replacement+document.substring(close);
    }
}
