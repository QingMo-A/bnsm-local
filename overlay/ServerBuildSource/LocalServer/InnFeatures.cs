using System.Security.Cryptography;
using System.Text;

internal sealed class LocalPubGame
{
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string,System.Text.Json.JsonElement>? ExtraFields {get;set;}
    public ulong Day {get;set;}
    public uint Promotion {get;set;}
    public uint Set {get;set;}
    public uint Round {get;set;}=1;
    public uint Remaining {get;set;}
    public uint Losses {get;set;}
    public uint Wins {get;set;}
    public uint PaidBypasses {get;set;}
    public uint QuestionIndex {get;set;}
    public uint Answer {get;set;}
    public uint WrongAnswer {get;set;}
    public PubminigameState State {get;set;}
}

internal sealed partial class ProtocolRouter
{
    private static int PubWeighted(IReadOnlyList<int> weights) {
        int total=weights.Sum(x=>Math.Max(0,x));if(total<=0)throw new InvalidDataException("Empty original pub probability row");
        int n=Random.Shared.Next(total);for(int i=0;i<weights.Count;i++){n-=Math.Max(0,weights[i]);if(n<0)return i;}throw new InvalidDataException();
    }
    private BscTable.PromotionMinigameData PubPromotion=>catalog.PubPromotions.Where(x=>x.number>0&&x.PromotionMinigameSetList.Any(s=>catalog.PubGames.Any(g=>g.SetID==s.SetID)))
        .OrderByDescending(x=>x.Priority).ThenBy(x=>x.ID).First();
    private void RefreshPub(LocalPlayer p) {
        ulong day=(Now+8*3600)/86400;var promotion=PubPromotion;
        if(p.PubGame.Day==day&&p.PubGame.Promotion==promotion.ID)return;
        var sets=promotion.PromotionMinigameSetList.Where(x=>x.Prob>0&&catalog.PubGames.Any(g=>g.SetID==x.SetID)).ToArray();
        p.PubGame=new(){Day=day,Promotion=(uint)promotion.ID,Set=(uint)sets[PubWeighted(sets.Select(x=>x.Prob).ToArray())].SetID,Remaining=(uint)promotion.number};
    }
    private static PkPubminigameRecord PubRecord(LocalPubGame game)=>new(){promotionId=game.Promotion,pubminigameId=game.Set,round=game.Round,
        remainCount=game.Remaining,loseCount=game.Losses,winCount=game.Wins,buyByPassCount=game.PaidBypasses,pubminigameIndex=game.QuestionIndex,
        // The native dice animation needs the result only after an answer.
        correctAnswer=game.State==PubminigameState.PUBMINIGAME_QUESTION?0:game.Answer,currentState=game.State,userIncorrectAnswer=game.WrongAnswer};
    private Reply? HandleInn(string name,object? request,LocalPlayer p,SessionKeyInfo session,byte[] payload,Func<object?,Reply> ok,Func<string,ServerErrorCode,Reply> fail)
    {
        if(name is not ("GS_GET_PUB_ENTRY_INFO_REQUEST" or "GS_GET_PUBMINIGAME_RECORD_REQUEST" or "GS_GET_PUBMINIGAME_QUIZ_REQUEST" or "GS_PUBMINIGAME_ANSWER_REQUEST" or "GS_BUY_PUBMINIGAME_BYPASS_REQUEST" or "GS_GET_SERVANT_MANAGE_INFO_REQUEST" or "GS_GET_SERVANT_TASK_COMPLETED_LIST_REQUEST" or "GS_GET_SERVANT_PVP_RECOMMEND_REQUEST"))return null;
        string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{session.loginSessionKey}/{name}/{session.sequenceNo}")));
        string fingerprint=Convert.ToHexString(SHA256.HashData(payload));
        if(p.InnReceipts.TryGetValue(key,out var receipt))return receipt.Fingerprint==fingerprint?new(receipt.Command,WireCodec.Deserialize(typeof(SessionKeyInfo).Assembly.GetType(receipt.TypeName,true),Convert.FromBase64String(receipt.Payload)),receipt.Name):fail("inn sequence reused with different data",ServerErrorCode.ERROR_INVALID_ID);
        if(p.Level<catalog.ContentValue(BscTable.CONTENT_OPEN_TYPE.CONTENTS_OPEN_ENTRANCE_TAVERN))return fail("inn unlock level not reached",ServerErrorCode.ERROR_INVALID_ID);
        RefreshPub(p);var game=p.PubGame;
        Reply Invalid(string why)=>fail(why,ServerErrorCode.ERROR_INVALID_ID);
        bool TryReward(uint cash,List<PKUserItem> items,List<PKUserCardInfo> cards,out PKCoinInfo earned) {
            earned=new();var row=PubPromotion.PromotionMinigameSetList.Single(x=>x.SetID==game.Set);int index=(int)game.Wins;
            if(index>=row.RewardId.Count||index>=row.RewardIdType.Count||index>=row.RewardQuantity.Count)return false;
            var grant=new ShopGrant();if(!AddShopReward(p,grant,row.RewardIdType[index],row.RewardId[index],row.RewardQuantity[index])||!CanPayAndGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_CASH,cash))return false;
            ApplyShopGrant(p,grant,BscTable.MONEY_TYPE.MONEY_TYPE_CASH,cash,items,cards);earned=new(){gold=(uint)grant.Gold,cashPoint=(uint)grant.Cash};game.Wins++;game.Round++;game.State=PubminigameState.PUBMINIGAME_NONE;return true;
        }
        Reply reply;
        switch(name) {
        case "GS_GET_PUB_ENTRY_INFO_REQUEST":reply=ok(new GsGetPubEntryInfoOk());break;
        case "GS_GET_PUBMINIGAME_RECORD_REQUEST":reply=ok(new GsGetPubminigameRecordOk{pubminigameRecord=PubRecord(game)});break;
        case "GS_GET_PUBMINIGAME_QUIZ_REQUEST":
            if(game.State==PubminigameState.PUBMINIGAME_NONE) {
                if(game.Remaining==0)return Invalid("all three daily inn attempts have been used");
                var questions=catalog.PubGames.Single(x=>x.SetID==game.Set).MinigameSetList;
                int index=PubWeighted(questions.Select(x=>x.QUIZProb).ToArray());var q=questions[index];
                game.QuestionIndex=(uint)index;game.Answer=(uint)PubWeighted(new[]{q.Prob_01,q.Prob_02,q.Prob_03,q.Prob_04,q.Prob_05,q.Prob_06})+1;
                game.Remaining--;game.WrongAnswer=0;game.State=PubminigameState.PUBMINIGAME_QUESTION;
            } else if(game.State!=PubminigameState.PUBMINIGAME_QUESTION)return Invalid("resolve the previous incorrect answer first");
            reply=ok(new GsGetPubminigameQuizOk{pubminigameRecord=PubRecord(game)});break;
        case "GS_PUBMINIGAME_ANSWER_REQUEST": {
            var r=(GsPubminigameAnswerRequest)request!;
            if(game.State!=PubminigameState.PUBMINIGAME_QUESTION||r.answerNumber is <1 or >6)return Invalid("no unanswered dice question or invalid dice face");
            var result=new GsPubminigameAnswerOk{correctAnswer=r.answerNumber==game.Answer,earnedExp=p.Exp,earnedCoinInfo=new()};
            if(result.correctAnswer) {
                if(!TryReward(0,result.earnedItemList,result.earnedCardList,out var coins))return Invalid("cannot fit original inn reward into inventory");result.earnedCoinInfo=coins;
            } else {game.Losses++;game.WrongAnswer=r.answerNumber;game.State=PubminigameState.PUBMINIGAME_INCORRECT_ANSWER;}
            result.pubminigameRecord=PubRecord(game);reply=ok(result);break;
        }
        case "GS_BUY_PUBMINIGAME_BYPASS_REQUEST": {
            var r=(GsBuyPubminigameBypassRequest)request!;if(game.State!=PubminigameState.PUBMINIGAME_INCORRECT_ANSWER)return Invalid("bypass is offered only after an incorrect answer");
            var result=new GsBuyPubminigameBypassOk{earnedExp=p.Exp,earnedCoinInfo=new()};
            if(r.useBypass) {
                var fee=catalog.Expenses.FindItem(BscTable.EXPENSE_WEIGHT_TYPE.EXPENSE_EXPEDIENT_MINIGAME,(int)game.PaidBypasses+1,true);
                if(fee==null||fee.Value<0||fee.MoneyType!=BscTable.MONEY_TYPE.MONEY_TYPE_CASH)return Invalid("invalid original bypass fee");
                if(p.CashPoint<fee.Value)return fail("not enough diamonds for inn bypass",ServerErrorCode.ERROR_NOT_ENOUGH_CASH_POINT);
                if(!TryReward((uint)fee.Value,result.earnedItemList,result.earnedCardList,out var coins))return Invalid("cannot fit original inn bypass reward");result.earnedCoinInfo=coins;game.PaidBypasses++;
            } else {game.Round++;game.State=PubminigameState.PUBMINIGAME_NONE;}
            result.pubminigameRecord=PubRecord(game);result.coinInfo=Coins(p);reply=ok(result);break;
        }
        // No online captures or raids are fabricated for a single-player profile.
        case "GS_GET_SERVANT_MANAGE_INFO_REQUEST":reply=ok(new GsGetServantManageInfoOk());break;
        case "GS_GET_SERVANT_TASK_COMPLETED_LIST_REQUEST":reply=ok(new GsGetServantTaskCompletedListOk());break;
        case "GS_GET_SERVANT_PVP_RECOMMEND_REQUEST":reply=ok(new GsGetServantPvpRecommendOk());break;
        default:return null;
        }
        if(p.InnReceipts.Count>=256)p.InnReceipts.Remove(p.InnReceipts.Keys.First());
        p.InnReceipts[key]=new(){Fingerprint=fingerprint,Command=reply.Command,Name=reply.Name,TypeName=reply.Message!.GetType().FullName!,Payload=Convert.ToBase64String(WireCodec.Serialize(reply.Message))};store.Save();return reply;
    }
}
