"""Inventory authored viewport surfaces without loading or modifying prefabs."""
import json,pathlib,re
root=pathlib.Path('I:/BnsmModernWork')
assets=root/'Project/Assets'
scripts={re.search(r'guid: (\w+)',p.read_text()).group(1):p.stem[:-3] for p in (assets/'Scripts/Assembly-CSharp').glob('*.cs.meta')}
rows=[];count=0
for file in (assets/'Resources/prefabs').rglob('*.prefab'):
    text=file.read_text(encoding='utf-8-sig');count+=1
    if not re.search(r'm_Name:.*(?:Dimd|Black|Shade|Dummy|Cover)',text,re.I):continue
    docs={int(n):b for n,b in re.findall(r'--- !u!\d+ &(\d+)\n(.*?)(?=--- !u!|\Z)',text,re.S)}
    transforms={int(re.search(r'm_GameObject: \{fileID: (\d+)',b).group(1)):n for n,b in docs.items() if b.startswith('Transform:')}
    def path(t):
        b=docs[t];g=int(re.search(r'm_GameObject: \{fileID: (\d+)',b).group(1));f=int(re.search(r'm_Father: \{fileID: (\d+)',b).group(1))
        name=re.search(r'  m_Name: (.*)',docs[g]).group(1)
        return (path(f)+'/' if f in docs else '')+name
    for g,t in transforms.items():
        p=path(t)
        if not re.search(r'dimd|black|shade|dummy|cover',p,re.I):continue
        match=re.search(r'm_LocalScale: \{x: ([\d.eE+-]+), y: ([\d.eE+-]+)',docs[t]);w,h=map(abs,map(float,match.groups()))
        if w<1280 or h<700:continue
        comps=[docs[int(n)] for n in re.findall(r'component: \{fileID: (\d+)\}',docs[g]) if int(n) in docs]
        types=[scripts.get(x,'?') for c in comps for x in re.findall(r'm_Script: .*guid: (\w+)',c)]
        if not any(x in types for x in ['UISprite','UITexture']):continue
        rows.append(dict(prefab=str(file.relative_to(assets)).replace('\\','/'),path=p,size=[w,h],types=types))
result=dict(prefabsScanned=count,modalCandidates=len(rows),prefabsWithCandidates=len(set(x['prefab'] for x in rows)),surfaces=rows)
(root/'Verification/legacy-viewport-audit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k!='surfaces'}))
