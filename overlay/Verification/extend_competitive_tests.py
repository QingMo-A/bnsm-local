from pathlib import Path
p=Path('I:/BnsmModernWork/Verification/BotWorldTests/CompetitiveRegression.cs');s=p.read_text(encoding='utf-8-sig');marker='var world=await peer.Request<GsGetWorldWarLobbyInfoOk>'
insert='''// A fresh request cannot repeat first-conquest rewards after ownership changes.
            stateBefore=State();await peer.Request<FailPacket>(GameServerCommand.GS_CASTLE_BATTLE_OCCUPY_MATCH_REQUEST,new GsCastleBattleOccupyMatchRequest{mapId=home,spotId=owned.Spot},success:false);Check(State()==stateBefore,"Owned castles reject repeated first-conquest attempts atomically");
            var nativeShop=CastleShopTest();await nativeShop;
            async Task CastleShopTest(){
                int slot=0;uint id=(uint)tile.GoodsId[slot],beforeGold=p.Gold;var buy=new GsBuyCastleStoreGoodsRequest{mapId=home,spotId=owned.Spot,SlotIndex=(uint)slot,goodsId=id};
                var goods=c.Goods.Single(x=>x.GoodsId==id);uint cost=(uint)Math.Ceiling(goods.Gold*tile.MoneyFactor[slot]);
                var bought=await peer.Request<GsBuyCastleStoreGoodsOk>(GameServerCommand.GS_BUY_CASTLE_STORE_GOODS_REQUEST,buy);Check(p.Gold==beforeGold-cost&&bought.castleStoreInfo.storeBuyCount==1,"Owned castle shop charges its original discounted price and saves sold stock");
                string before=State();await peer.Request<GsBuyCastleStoreGoodsOk>(GameServerCommand.GS_BUY_CASTLE_STORE_GOODS_REQUEST,buy,retry:true);Check(State()==before,"Castle shop retry cannot duplicate goods or payment");
            }
            foreach(var pair in new[]{(Mode:10u,Kind:1),(Mode:12u,Kind:2),(Mode:31u,Kind:7),(Mode:42u,Kind:14)}) {
                p.VipExp=(uint)c.Vip.Max(x=>x.LvUpNeedVipExp);uint before=p.CashPoint;var buy=new GsBuyBonusCountRequest{priceType=(BscTable.VIP_PRICE_DISCOUNT_TYPE)pair.Kind,buyCount=1};
                var paid=await peer.Request<GsBuyBonusCountOk>(GameServerCommand.GS_BUY_BONUS_COUNT_REQUEST,buy);Check(paid.changedValue==1&&p.Competitive.Records[pair.Mode].PaidLeft==1&&p.CashPoint<before,"Competitive mode "+pair.Mode+" buys a native paid entry and preserves the free quota");
                stateBefore=State();await peer.Request<GsBuyBonusCountOk>(GameServerCommand.GS_BUY_BONUS_COUNT_REQUEST,buy,retry:true);Check(State()==stateBefore,"Paid competitive ticket retry is idempotent for mode "+pair.Mode);
            }
            var rewardRow=catalog.Rewards.First(x=>x.PvpBattleType==(BscTable.PVP_BATTLE_TYPE)9&&x.BattleResultType==BscTable.BATTLE_RESULT_TYPE.BATTLE_RESULT_SCORE);p.Competitive.Records[31].RewardPoints=(uint)rewardRow.Score;
            var claim=new GsGetInfinitArenaTodayRewardRequest{rewardId=(uint)rewardRow.Id};await peer.Request<GsGetInfinitArenaTodayRewardOk>(GameServerCommand.GS_GET_INFINIT_ARENA_TODAY_REWARD_REQUEST,claim);Check(p.Competitive.Records[31].RewardClaims.Contains((uint)rewardRow.Id),"Infinite arena score reward records its original catalog claim");
            stateBefore=State();await peer.Request<GsGetInfinitArenaTodayRewardOk>(GameServerCommand.GS_GET_INFINIT_ARENA_TODAY_REWARD_REQUEST,claim,retry:true);Check(State()==stateBefore,"Infinite score reward retry cannot duplicate rewards");
            '''
assert marker in s;s=s.replace(marker,insert+marker)
p.write_text(s,encoding='utf-8-sig')
