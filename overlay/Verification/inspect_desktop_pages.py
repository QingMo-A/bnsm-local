import pathlib,json
root=pathlib.Path('I:/BnsmModernWork/Verification')
folder=max((x for x in root.glob('desktop-pages-*') if x.is_dir()),key=lambda x:x.stat().st_mtime)
print(folder)
for name in ['board','tower','quests','book','bag','stage','deck']:
    p=folder/(name+'-85.json')
    if not p.exists():continue
    rows=json.loads(p.read_text(encoding='utf-8-sig'))
    print('\nPAGE',name)
    for row in rows:
        path=row['path'];parts=path.split('/')
        if not row['active']:continue
        if row['clip'] or row['list'] or (len(parts)<8 and (row['text'] or any(x in path for x in ['Dice','Character','Minimi','Card','Root','Root','Tab','Sorting','Bottom','BG_Battle','Stage','Camera']))):
            print(path,'p=',[round(x,2) for x in row['pos']],'s=',[round(x,2) for x in row['scale']],'pixel=',[round(x,1) for x in row['pixel']], 'clip=',row['clip'],'list=',row['list'],'text=',(row['text'] or '')[:40])
