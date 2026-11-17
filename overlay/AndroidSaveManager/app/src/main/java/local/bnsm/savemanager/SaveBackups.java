package local.bnsm.savemanager;

import java.io.*;
import java.nio.file.Files;
import java.nio.file.StandardCopyOption;
import java.time.Instant;
import java.time.ZoneOffset;
import java.time.format.DateTimeFormatter;
import java.util.*;

/** App-private, immutable copies of the game's validated save exports. */
final class SaveBackups {
    static final long MAX_BYTES=128L*1024*1024;
    private static final DateTimeFormatter STAMP=DateTimeFormatter.ofPattern("uuuuMMdd-HHmmss",Locale.ROOT).withZone(ZoneOffset.UTC);
    private final File root;

    SaveBackups(File root){this.root=root;}

    static final class Entry {
        final File archive;
        final String name;
        final Instant created;
        Entry(File archive,String name,Instant created){this.archive=archive;this.name=name;this.created=created;}
    }

    Entry create(File source,String name,Instant created)throws IOException {
        ensureDirectory(root);
        String id="save-backup-"+STAMP.format(created)+"-"+UUID.randomUUID().toString().replace("-","");
        File stage=Files.createTempDirectory(root.toPath(),".pending-").toFile(),target=new File(root,id);
        File archive=new File(stage,id+".bnsm-save.zip"),metadata=new File(stage,"backup.properties");
        try{
            copy(source,archive);
            Properties description=new Properties();description.setProperty("name",name);description.setProperty("created",created.toString());
            try(FileOutputStream out=new FileOutputStream(metadata)){description.store(out,null);out.getFD().sync();}
            Files.move(stage.toPath(),target.toPath(),StandardCopyOption.ATOMIC_MOVE);
            return new Entry(new File(target,archive.getName()),name,created);
        }finally{archive.delete();metadata.delete();stage.delete();}
    }

    List<Entry> list()throws IOException {
        if(!root.exists())return Collections.emptyList();
        File[] dirs=root.listFiles(File::isDirectory);if(dirs==null)throw new IOException("无法读取备份目录");
        List<Entry> entries=new ArrayList<>();
        for(File dir:dirs){
            if(!dir.getName().matches("save-backup-[0-9]{8}-[0-9]{6}-[0-9a-f]{32}"))continue;
            File archive=new File(dir,dir.getName()+".bnsm-save.zip");
            if(!archive.isFile()||archive.length()==0||archive.length()>MAX_BYTES)continue;
            Properties metadata=new Properties();
            try(InputStream in=new FileInputStream(new File(dir,"backup.properties"))){
                metadata.load(in);entries.add(new Entry(archive,metadata.getProperty("name","未命名存档"),Instant.parse(metadata.getProperty("created"))));
            }catch(IOException|RuntimeException ignored){/* An incomplete entry must not hide healthy backups. */}
        }
        entries.sort(Comparator.comparing((Entry e)->e.created).thenComparing(e->e.archive.getName()).reversed());
        return entries;
    }

    File shareCopy(Entry entry,File cache)throws IOException {
        ensureDirectory(cache);
        File target=new File(cache,entry.archive.getName());
        if(target.isFile()&&target.length()==entry.archive.length())return target;
        File stage=File.createTempFile("backup-share-",".partial",cache);
        try{copy(entry.archive,stage);Files.move(stage.toPath(),target.toPath(),StandardCopyOption.ATOMIC_MOVE,StandardCopyOption.REPLACE_EXISTING);return target;}
        finally{stage.delete();}
    }

    private static void ensureDirectory(File file)throws IOException {
        if(!file.isDirectory()&&!file.mkdirs()&&!file.isDirectory())throw new IOException("无法创建备份文件夹");
    }

    private static void copy(File source,File target)throws IOException {
        long size=source.length();
        if(size==0||size>MAX_BYTES)throw new IOException("存档备份必须大于 0 且不超过 128 MiB");
        try(InputStream in=new FileInputStream(source);FileOutputStream out=new FileOutputStream(target)){
            byte[] buffer=new byte[65536];int count;long total=0;
            while((count=in.read(buffer))!=-1){if((total+=count)>size)throw new IOException("存档大小已变化，请重试");out.write(buffer,0,count);}
            if(total!=size)throw new IOException("存档备份不完整，请重试");
            out.getFD().sync();
        }
    }
}
