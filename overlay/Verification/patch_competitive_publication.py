from pathlib import Path
p=Path('I:/BnsmModernWork/Verification/verify_competitive_native.py');s=p.read_text(encoding='utf-8-sig');old="(root/('Verification/competitive-native-'+mode+'-latest.json')).write_text";new='''result['serverCoreSha256']=hashlib.sha256((root/'Verification/BotWorldTests/bin/Release/net10.0/Bnsm.ServerCore.dll').read_bytes()).hexdigest()
    player=json.loads((folder/'saves/players.json').read_text(encoding='utf-8-sig'))[0];state=player['Competitive']
    if mode in ['arena','rank','balance','infinite']:
        number={'arena':'12','rank':'10','balance':'42','infinite':'31'}[mode];result['persistedSettlement']=state['Records'].get(number,{}).get('Battles')==1
    elif mode=='castle':result['persistedSettlement']=state['Castles']['1/1']['Status']==1
    else:result['persistedSettlement']=state['WorldScores']['6']['Daily']>0 and state['ActiveMode']==0
    if mode=='arena':result['ammunitionConsumedOnce']=next(x for x in player['EquipmentItems'] if x['Sequence']==player['CardStates']['1']['WeaponSequence'])['BsBallCount']==40;result['passed'] &= result['ammunitionConsumedOnce']
    result['passed'] &= result['persistedSettlement']
    '''+old;assert old in s;s=s.replace(old,new);p.write_text(s,encoding='utf-8-sig')
p=Path('I:/BnsmModernWork/publish_windows_release.py');s=p.read_text(encoding='utf-8-sig');s=s.replace('require_skill_enchant=False):','require_skill_enchant=False,require_competitive=False):');mark="    assert all(p['passed'] for p in proof.values())";s=s.replace(mark,'''    if require_competitive:
        for name in ['competitive-server-latest.json']+['competitive-native-'+mode+'-latest.json' for mode in ['arena','rank','balance','castle','world','infinite']]:
            proof[name]=json.loads((ROOT/'Verification'/name).read_text('utf8'))
            assert proof[name]['serverCoreSha256']==digest(STAGE/HOST/'Bnsm.ServerCore.dll'),'Competitive server proof does not match published binary: '+name
    '''+mark.strip());mark="    for name in ['windows-full-native-latest.json']";s=s.replace(mark,"    if require_competitive:\n        for mode in ['arena','rank','balance','castle','world','infinite']:assert proof['competitive-native-'+mode+'-latest.json']['clientAssemblySha256']==assembly_hash,'Competitive native proof does not match client'\n"+mark);s=s.replace("require_skill_enchant='--require-skill-enchant' in sys.argv)","require_skill_enchant='--require-skill-enchant' in sys.argv,require_competitive='--require-competitive' in sys.argv)");p.write_text(s,encoding='utf8')
