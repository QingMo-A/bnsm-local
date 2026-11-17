from pathlib import Path
p=Path('I:/BnsmModernWork/Project/Assets/Scripts/Assembly-CSharp/BscPrivateSiegeBattleDialog.cs');s=p.read_text(encoding='utf-8-sig');old='if(_BaseBlocks==null)_BaseBlocks=find("BaseBlockRoot");';new=old+'''
        if(_EnemyInfo==null){var n=find("EnemyInfo");if(n!=null)_EnemyInfo=n.gameObject;}
        if(_Label_Title==null){var n=find("Label_Title");if(n!=null)_Label_Title=n.GetComponent<UILabel>();}
        if(coProfile==null){var n=find("Profile");if(n!=null)coProfile=n.GetComponent<UIObjectCreator>();}
        if(_Label_EnemyNickName==null){var n=find("Label_NickName");if(n!=null)_Label_EnemyNickName=n.GetComponent<UILabel>();}
        if(_Label_EnemyLevel==null){var n=find("Number");if(n!=null)_Label_EnemyLevel=n.GetComponent<UILabel>();}
        if(_BgCashBattleResult==null){var n=find("BG_Result");if(n!=null)_BgCashBattleResult=n.gameObject;}
        if(_FrameLT==null){var n=find("Frame_1");if(n!=null)_FrameLT=n.GetComponent<UISprite>();}
        if(_FrameRT==null){var n=find("Frame_2");if(n!=null)_FrameRT=n.GetComponent<UISprite>();}
        if(_FrameLB==null){var n=find("Frame_3");if(n!=null)_FrameLB=n.GetComponent<UISprite>();}
        if(_FrameRB==null){var n=find("Frame_4");if(n!=null)_FrameRB=n.GetComponent<UISprite>();}
''';assert old in s;s=s.replace(old,new);p.write_text(s,encoding='utf-8-sig')
