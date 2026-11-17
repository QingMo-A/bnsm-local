using System.Security.Cryptography;
using System.Text.Json;
using live2d;

internal static class OriginalCardRegression
{
    private sealed class HeadlessModel:ALive2DModel
    {
        public override void deleteTextures(){}
        public override void draw(){}
        public override DrawParam getDrawParam()=>null!;
    }
    internal static void Run(string root)
    {
        var checks=new List<string>();void Check(bool value,string note){if(!value)throw new InvalidDataException(note);checks.Add(note);Console.WriteLine("PASS "+note);}
        var original=new GameCatalog(root,false);var extended=new GameCatalog(root,false,true);
        var source=original.Cards.Single(x=>x.Id==LocalOriginalCardDefinition.SourceCardId);
        var card=extended.Cards.Single(x=>LocalOriginalCardDefinition.IsCard(x.Id));
        using (var input=File.OpenRead(Path.Combine(root,"LocalServer/data/tables/CardDataTable.pb")))
        {
            var native=ProtoBuf.Serializer.Deserialize<BscTable.CardDataTable>(input).List;
            Check(original.Cards.Take(native.Count).Select(row=>row.Id).SequenceEqual(native.Select(row=>row.Id))
                &&native.All(row=>WireCodec.Serialize(row).SequenceEqual(WireCodec.Serialize(original.RequireCard(row.Id)))),
                "All native card definitions enter the registry in their original order with byte-equivalent data");
        }
        Check(original.Cards.All(row=>ReferenceEquals(row,original.FindCard(row.Id)))
            &&ReferenceEquals(card,extended.FindCard(card.Id))&&original.FindCard(int.MaxValue)==null,
            "Original, production custom and opt-in prototype cards use the same ID lookup");
        bool rejectedCollision=false;
        try{_ = new LocalCardRegistry.CardCatalog(new[]{LocalHongCard.FirstCardId},(_,_)=>source,true,false);}
        catch(InvalidDataException){rejectedCollision=true;}
        Check(rejectedCollision,"The registry rejects a generated ID already present in the source tables");
        Check(!original.Cards.Any(x=>LocalOriginalCardDefinition.IsCard(x.Id)),"Normal server catalog excludes the opt-in prototype");
        Check(extended.Cards.Count==original.Cards.Count+1&&extended.Skills.Count==original.Skills.Count+2,"Prototype adds one independent card and two skill IDs");
        Check(card.EvolveFirstAncestor==card.Id&&card.MaxRank==card.Rank&&card.RankUpCardId==0,"Prototype identity is independent and has no broken evolution links");
        Check(WireCodec.Serialize(source).SequenceEqual(WireCodec.Serialize(extended.Cards.Single(x=>x.Id==source.Id))),"Original source row remains byte-identical");
        Check(extended.UsableHeroes.All(x=>!LocalOriginalCardDefinition.IsCard(x.Id))&&extended.UsableHeroes.Count()==original.UsableHeroes.Count(),"Prototype never enters normal gacha or resident build pools");
        Check(card.ActiveSkillId.SequenceEqual(new[]{LocalOriginalCardDefinition.FirstSkillId,LocalOriginalCardDefinition.FirstSkillId+1})&&card.ActiveSkillId.All(extended.SkillIds.Contains),"Both client/server skill IDs resolve to complete native effect data");
        string modelPath="I:/BnsmModernWork/Prototypes/ShenYan/shenyan.moc";
        var model=new HeadlessModel();using(var input=File.OpenRead(modelPath))ALive2DModel.loadModel_exe(model,input);
        Check(model.getErrorFlags()==0&&model.getCanvasWidth()==1024&&model.getCanvasHeight()==1536,"Installed original Live2D reader accepts the authored moc with valid EOF and canvas");
        model.setParamFloat("PARAM_BREATH",0);model.setParamFloat("PARAM_ANGLE_X",0);model.update();var a=model.getTransformedPoints(0).ToArray();
        model.setParamFloat("PARAM_BREATH",1);model.update();var b=model.getTransformedPoints(0).ToArray();
        Check(a.Length==850&&a.Zip(b).Any(x=>Math.Abs(x.First-x.Second)>1)&&b.All(float.IsFinite),"Breath parameter really deforms the 425-vertex Live2D mesh");
        model.setParamFloat("PARAM_ANGLE_X",30);model.update();var c=model.getTransformedPoints(0);
        Check(c.Zip(b).Any(x=>Math.Abs(x.First-x.Second)>1)&&c.All(float.IsFinite)&&model.getIndexArray(0).Length==2304,"Wind parameter deforms the cloth mesh with valid native triangles");
        Check(b.Where((value,i)=>i%2==1).Zip(a.Where((value,i)=>i%2==1)).Any(x=>Math.Abs(x.First-x.Second)>1)&&c.Where((value,i)=>i%2==1).Zip(b.Where((value,i)=>i%2==1)).All(x=>Math.Abs(x.First-x.Second)<.01),"Breath moves the torso vertically while wind keeps authored vertical coordinates");
        var report=new{passed=true,checks,cardId=card.Id,sourceCardId=source.Id,normalSaveUsed=false,fullyLayeredCubismModel=false,sourceRigReused=true,serverCoreSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(LocalRuntime).Assembly.Location))).ToLowerInvariant()};
        File.WriteAllText("I:/BnsmModernWork/Verification/original-card-server-latest.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
    }
    internal static async Task Fixture(string root,string folder)
    {
        folder=Path.GetFullPath(folder);if(!folder.StartsWith("I:\\BnsmModernWork\\Verification\\",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Isolated verification required");
        Directory.CreateDirectory(folder);
        await using var r=new LocalRuntime(root,Path.Combine(folder,"saves"),0,0,0,false,enableOriginalCardPrototype:true);
        if(r.Store.All().Count==0){
            CompetitiveRegression.Prepare(r);var p=r.Store.All().Single();var card=r.Catalog.Cards.Single(x=>LocalOriginalCardDefinition.IsCard(x.Id));
            p.CardIds[0]=(uint)card.Id;
            var weapon=r.Catalog.Items.Where(x=>x.ItemType==BscTable.ITEM_TYPE.ITEM_TYPE_EQUIPMENT&&x.ItemSubType==BscTable.ITEM_SUB_TYPE.ITEM_SUB_TYPE_WEAPON&&LocalBotBuilder.Fits(x,card,76)).OrderByDescending(x=>x.Step).ThenByDescending(x=>x.Rank).First();
            ulong seq=p.NextEquipmentSequence++;p.EquipmentItems.Add(new(){ItemId=(uint)weapon.Id,Sequence=seq,RegisteredAt=ProtocolRouter.Now});
            p.CardStates[1]=new(){Level=76,Exp=ProgressionRules.CardFloor(r.Catalog,76),Enchant=5,WeaponSequence=seq,ActiveSkills=[76,76],PassiveSkills=[76,76,76,76,76],SkillEnchants=[0,0,0,0,0,0,0]};
            r.Store.Save();
        }
        File.WriteAllText(Path.Combine(folder,"endpoints.json"),JsonSerializer.Serialize(new{directoryPort=r.DirectoryPort}));
        await Task.Delay(Environment.GetEnvironmentVariable("BNSM_ORIGINAL_CARD_INTERACTIVE")=="1"?Timeout.InfiniteTimeSpan:TimeSpan.FromMinutes(30));
    }
}
