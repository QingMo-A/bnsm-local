"""Bulk-import the current source tree using Git's native pack importer and LFS."""
from pathlib import Path
import hashlib, json, os, shutil, subprocess, time
root=Path(__file__).resolve().parents[1]
os.chdir(root)
def git(*args,input=None):return subprocess.check_output(['git',*args],input=input)
assert git('symbolic-ref','--short','HEAD').decode().strip()=='main'
assert not git('diff','--cached','--name-only').strip(),'Preserve any existing staged work'
lock=root/'.git/index.lock'
if lock.exists():
    # This runner is used only after the owned git-add process was stopped.
    raw=subprocess.run(['powershell.exe','-NoProfile','-Command',"Get-Process git,git-lfs -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id"],stdout=subprocess.PIPE,stderr=subprocess.PIPE,creationflags=subprocess.CREATE_NO_WINDOW).stdout.strip()
    assert not raw,'Another Git writer may still be active'
    lock.unlink()
old=git('rev-parse','HEAD').decode().strip()
files=sorted(set(p for p in git('ls-files','-z','--cached','--others','--exclude-standard').split(b'\0') if p))
files=[p for p in files if (root/os.fsdecode(p)).is_file()]
assert len(files)>40000,len(files)
assert all(not (os.fsdecode(p).endswith(('.keystore','.jks','.pfx','.p12')) or Path(os.fsdecode(p)).name in ['players.json','bot-world.json']) for p in files)
raw=git('check-attr','-z','--stdin','filter','text','eol',input=b'\0'.join(files)+b'\0').split(b'\0')
attrs={}
for i in range(0,len(raw)-1,3):attrs.setdefault(raw[i],{})[raw[i+1].decode()]=raw[i+2].decode()
try:autocrlf=git('config','--get','core.autocrlf').decode().strip()
except subprocess.CalledProcessError:autocrlf='false'
identity=git('var','GIT_COMMITTER_IDENT').strip()
branch='refs/heads/import/v28-baseline-bulk'
message='基线：v28 可用版本，纳入 Unity 源工程、Git LFS 素材与验证程序\n'.encode()
objects={};lfs_count=lfs_bytes=0;started=time.monotonic()
out=root/'Verification/git-baseline-import.log'
with out.open('wb') as log:
    process=subprocess.Popen(['git','fast-import','--quiet','--done'],stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,bufsize=1024*1024)
    pipe=process.stdin
    pipe.write(b'commit '+branch.encode()+b'\ncommitter '+identity+b'\ndata '+str(len(message)).encode()+b'\n'+message+b'\nfrom '+old.encode()+b'\ndeleteall\n')
    for index,p in enumerate(files):
        path=root/os.fsdecode(p);data=path.read_bytes();a=attrs[p]
        if a['filter']=='lfs':
            oid=hashlib.sha256(data).hexdigest();size=len(data)
            target=root/'.git/lfs/objects'/oid[:2]/oid[2:4]/oid
            if not target.exists():
                target.parent.mkdir(parents=True,exist_ok=True)
                temporary=target.with_suffix('.importing')
                temporary.write_bytes(data);assert hashlib.sha256(temporary.read_bytes()).hexdigest()==oid
                temporary.replace(target)
            assert target.stat().st_size==size
            data=f'version https://git-lfs.github.com/spec/v1\noid sha256:{oid}\nsize {size}\n'.encode()
            lfs_count+=1;lfs_bytes+=size
        elif a['text']!='unset' and (a['text']=='set' or a['eol']!='unspecified' or autocrlf in ['true','input']):
            if b'\0' not in data[:8000]:data=data.replace(b'\r\n',b'\n')
        objects[p]=hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest()
        quoted=b'"'+p.replace(b'\\',b'\\\\').replace(b'"',b'\\"').replace(b'\n',b'\\n')+b'"'
        pipe.write(b'M 100644 inline '+quoted+b'\ndata '+str(len(data)).encode()+b'\n'+data+b'\n')
        if index%5000==0:print(json.dumps(dict(files=index,total=len(files),lfs=lfs_count,seconds=round(time.monotonic()-started))),flush=True)
    pipe.write(b'\ndone\n');pipe.close()
    assert process.wait(timeout=600)==0,out
candidate=git('rev-parse',branch).decode().strip()
tree={}
for row in git('ls-tree','-r','-z',candidate).split(b'\0'):
    if row:
        meta,path=row.split(b'\t',1);tree[path]=meta.split()[2].decode()
assert tree==objects,'Imported tree differs from the source snapshot'
git('update-ref','refs/heads/main',candidate,old)
git('read-tree',candidate)
assert git('write-tree').strip()==git('rev-parse',candidate+'^{tree}').strip()
git('update-ref','-d',branch,candidate)
report=dict(passed=True,commit=candidate,parent=old,files=len(files),lfsFiles=lfs_count,lfsInputBytes=lfs_bytes,sourceTreeVerified=True,worktreeRewritten=False,remoteConfigured=False,seconds=round(time.monotonic()-started,1))
(root/'Verification/git-baseline-import.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),'utf-8')
print(json.dumps(report,ensure_ascii=False),flush=True)
