"""Inspect the single-APK trial and bind it to its tested embedded input set."""
from pathlib import Path
import argparse,hashlib,json,re,struct,subprocess,zipfile,io
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
CACHED_APK=Path('C:/BnsmModernBuildCache/IntegratedAndroid/GradleExport/launcher/build/outputs/apk/release/launcher-release.apk')
APK=CACHED_APK if CACHED_APK.exists() else ROOT/'Builds/Android-Integrated/BnsmLocal-integrated-arm64.apk'
TOOLS=Path('C:/Android/build-tools/36.0.0')
JAVA=Path('I:/BnsmTools/Unity6.3/6000.3.27f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin/java.exe')
OUT=ROOT/'Verification/android-integrated-phone'
parser=argparse.ArgumentParser()
parser.add_argument('--require-branding',action='store_true')
parser.add_argument('--require-shop-edges',action='store_true')
parser.add_argument('--require-export-performance',action='store_true')
parser.add_argument('--require-helper-costumes',action='store_true')
parser.add_argument('--require-adventure-drop',action='store_true')
parser.add_argument('--require-story',action='store_true')
parser.add_argument('--require-castle-raid-friends',action='store_true')
parser.add_argument('--require-universal-fragment-policy',action='store_true')
parser.add_argument('--require-shared-card-catalog',action='store_true')
parser.add_argument('--require-story-closed',action='store_true')
parser.add_argument('--require-chain-battle',action='store_true')
options=parser.parse_args()
inputs=json.loads((ROOT/'Verification/android-embedded-inputs.json').read_text())
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def command(args):return subprocess.check_output(list(map(str,args)),stderr=subprocess.STDOUT).decode('utf-8',errors='replace')

native=[]
branding=False
with zipfile.ZipFile(APK) as apk:
    assert apk.testzip() is None
    names=apk.namelist()
    assert sorted({n.split('/')[1] for n in names if n.startswith('lib/') and n.endswith('.so')})==['arm64-v8a']
    assert not any('monodroid' in n or 'libcoreclr' in n for n in names)
    content=next(n for n in names if n=='assets/embedded-server.zip')
    assert hashlib.sha256(apk.read(content)).hexdigest()==inputs['contentSha256']
    metadata=apk.read(next(n for n in names if n.endswith('/global-metadata.dat')))
    assert b'AndroidBotSaveRecovery' in metadata,'Numeric recovery is missing from the compiled embedded core'
    assert b'ExportForSupport' in metadata,'Host-gated console snapshot capture is missing from the compiled core'
    if options.require_chain_battle:
        assert all(marker in metadata for marker in [b'HandleChainBattle',b'EndChainBattle',b'LocalChainBattleStartOk',b'LocalChainBattleEndRequest'])
    if options.require_castle_raid_friends:
        assert all(marker in metadata for marker in [b'CastleRaidBotDamage',b'LocalCastleRaidHelpRequest',b'OpenBotHelpPicker']), 'Castle raid bot assistance is missing from the compiled game or server'
    if options.require_universal_fragment_policy:
        assert all(marker in metadata for marker in [b'AllowsUniversalFragments',b'GetUsableUniversalFragments',b'OriginalFamiliesRequiringOwnFragments']), 'The per-family universal fragment policy is missing from the compiled game or server'
    if options.require_shared_card_catalog:
        assert all(marker in metadata for marker in [b'CardCatalog',b'FindExtensionForCard',b'TranscendenceRows']), 'The shared card registry is missing from the compiled game or server'
    if options.require_story_closed:
        assert b'IsBlockedStage' in metadata and any('江湖篇章暂未开放。'.encode(encoding) in metadata
                                                   for encoding in ('utf-8', 'utf-16le'))
    if options.require_story:
        assert all(marker in metadata for marker in [b'LocalStoryChapter',b'LocalStoryMap',b'CloseForBattlePreview',b'SettleStory',b'LocalHongCard',b'STalentWeight',b'LocalCardRegistry',b'CardFacePath',b'FramedPortrait',b'GetCardBookIds',b'FindMarketPreview',b'LocalHongRelations',b'LocalHongEquipment',b'ApplyPortrait',b'CoverPortrait',b'ShowSelectedPortrait',b'CreateEnchantGroups',b'CreateExtraEnchantEffect',b'TranscendenceRows',b'TryShowHongRainPortrait',b'StoryProgress',b'SweepStory'])
        story_catalog=(ROOT/'Project/Assets/Resources/LocalStoryCatalog.json').read_bytes()
        story_resources=apk.read(next(n for n in names if n.endswith('/Bnsm.EmbeddedServer.dll-resources.dat')))
        assert b'LocalStoryCatalog.json' in story_resources and story_catalog in story_resources
    if options.require_shop_edges:
        assert b'ArrangeShopArrow' in metadata and b'get_GoodsArea' in metadata,'Shared shop edge layout is missing from IL2CPP output'
    if options.require_export_performance:
        assert b'lastSupportExport' in metadata,'Host export timing capture is missing from IL2CPP output'
    if options.require_helper_costumes:
        assert all(name in metadata for name in [b'LocalHelperCostumeDefinition',b'LocalHelperCostumeTables',b'CraftHelperCostume',b'LoadModel']), 'Shared helper wardrobe or native crafting is missing from IL2CPP output'
    if options.require_adventure_drop:
        assert b'AdventureDropChanceMultiplier' in metadata and b'LocalAdventureDropRules' in metadata and b'BscEnterStageRewardListItem' in metadata,'Adventure multiplier is missing from IL2CPP output'
    dex=b''.join(apk.read(n) for n in names if re.fullmatch(r'classes\d*\.dex',n))
    assert b'JsonDocumentPatch' in dex and b'logsOnly' in dex,'Lossless queue or startup log export is missing from Java output'
    assert b'SaveArchiveReader' in dex and b'receiveImport' in dex and b'importProfile' in dex,'Offline manager import bridge missing from Java output'
    assert b'bundleInfo' in dex and b'saveOmissionReason' in dex,'Verified export content summary is missing from Java output'
    if options.require_export_performance:
        assert all(x in dex for x in [b'putStoredSnapshot',b'exportPerformance',b'snapshotElapsedMs',b'saveValidationMs',b'exportProgress']),'Fast streamed export or timing stages are missing from Java output'
    branding=all(text.encode('utf-8') in metadata or text.encode('utf-16le') in metadata
        for text in ['LocalBranding','Local Edition · By QingMo','移植与维护：QingMo'])
    if options.require_branding:assert branding,'Branding missing from the packaged IL2CPP metadata'
    for name in names:
        if not name.startswith('lib/') or not name.endswith('.so'):continue
        with apk.open(name) as stream:
            header=stream.read(64);assert header[:6]==b'\x7fELF\x02\x01'
            offset=struct.unpack_from('<Q',header,32)[0];size,count=struct.unpack_from('<HH',header,54)
            stream.seek(offset);program=stream.read(size*count)
        alignments=[struct.unpack_from('<IIQQQQQQ',program,i*size)[7] for i in range(count) if struct.unpack_from('<I',program,i*size)[0]==1]
        assert alignments and min(alignments)>=16384,(name,alignments)
        native.append(dict(file=name,loadAlignments=alignments))
command([TOOLS/'zipalign.exe','-c','-P','16','4',APK])
signature=command([JAVA,'-jar',TOOLS/'lib/apksigner.jar','verify','--verbose','--print-certs',APK])
badging=command([TOOLS/'aapt.exe','dump','badging',APK])
assert "package: name='local.bnsm.integratedtest'" in badging
assert "targetSdkVersion:'36'" in badging
version_code=int(re.search(r"versionCode='(\d+)'",badging).group(1))
assert version_code>=38
app_label=re.search(r"application-label:'([^']*)'",badging).group(1)
icons=json.loads((ROOT/'AndroidGameIcon/origin.json').read_text('utf-8'))
icon_sources={}
for item in icons['entries']:
    path=ROOT/'AndroidGameIcon'/item['path'];assert sha(path)==item['sha256']
    with Image.open(path) as picture:icon_sources[picture.size]=picture.convert('RGBA').tobytes()
resolved_icons=[]
with zipfile.ZipFile(APK) as package:
    for density,name in re.findall(r"application-icon-(\d+):'([^']+)'",badging):
        assert name.endswith('.png'),'Launcher still resolves to generated adaptive XML'
        with Image.open(io.BytesIO(package.read(name))) as picture:
            assert picture.size in icon_sources and picture.convert('RGBA').tobytes()==icon_sources[picture.size],name
            resolved_icons.append(dict(density=int(density),size=picture.size,path=name))
assert len(resolved_icons)>=5,'Original launcher icon density variants missing'
if options.require_branding:
    assert version_code>=30 and app_label=='战斗吧剑灵 · 内置服体验'
regression=json.loads((ROOT/'Verification/android-embedded-regressions/result.json').read_text())
assert regression['passed'] and len(regression['checks'])>=15 and any(x['name']=='world-war-test' and x['exitCode']==0 for x in regression['checks'])
if options.require_chain_battle:
    assert any(x['name']=='chain-test' and x['exitCode']==0 for x in regression['checks'])
core=next(x for x in inputs['libraries'] if x['file']=='Bnsm.EmbeddedServer.dll')['sha256']
assert regression['coreSha256']==core
numeric=json.loads((ROOT/'Verification/android-manager-contract/numeric-result.json').read_text())
java=json.loads((ROOT/'Verification/android-manager-contract/java-result.json').read_text())
assert numeric['passed'] and numeric['coreSha256']==core and len(numeric['checks'])>=14
assert java['passed'] and java['losslessBotQueue'] and java['jsonRuntime'].startswith('Android AOSP')
console_export=json.loads((ROOT/'Verification/android-manager-contract/console-export-result.json').read_text())
assert console_export['passed'] and console_export['checks']>=11 and console_export['coreSha256']==core
adapter=(ROOT/'Project/Assets/Scripts/Assembly-CSharp/LocalEmbeddedServer.cs').read_text('utf-8')
export_command=adapter[adapter.index('else if (command == "export")'):adapter.index('else if (command.StartsWith("import:"')]
assert 'ExportForSupport(snapshot)' in export_command and 'if(!Ready)' not in export_command and 'if (!Ready)' not in export_command
for source in inputs['sourceManifest']:assert sha(ROOT/source['source'])==source['sha256']
export=Path('C:/BnsmModernBuildCache/IntegratedAndroid/GradleExport')
if export.exists():
    for name in ['LocalSupport.java','EmbeddedService.java','SaveBridge.java','SharedWorkspace.java','JsonDocumentPatch.java','SaveArchiveReader.java','ExportProvider.java','ManagementActions.java','SaveManagerGateway.java']:
        assert sha(ROOT/'Project/Assets/Plugins/Android'/name)==sha(export/'unityLibrary/src/main/java/local/bnsm/bridge'/name)
result=dict(passed=True,apk=str(APK),package='local.bnsm.integratedtest',versionCode=version_code,appLabel=app_label,bytes=APK.stat().st_size,
    sha256=sha(APK),embeddedCoreSha256=core,contentSha256=inputs['contentSha256'],signatureVerified=True,
    arm64Only=True,elf16KiBAligned=True,apk16KiBAligned=True,nativeLibraries=native,
    authoritativeSourceFilesVerified=len(inputs['sourceManifest']),portableRegressionCases=len(regression['checks']),
    javaExportSourceVerified=export.exists(),brandingVerified=branding,originalLauncherIconVerified=True,launcherIcons=resolved_icons,
    losslessBotQueueVerified=True,botStartupRecoveryChecks=len(numeric['checks']),androidAospContractChecks=java['checks'],
    consoleExportChecks=console_export['checks'],hostGatedSupportExport=True,verifiedNestedSave=True)
if options.require_helper_costumes:
    native=json.loads((ROOT/'Verification/helper-costume-native-latest.json').read_text())
    portable=json.loads((ROOT/'Verification/helper-costume-portable-latest.json').read_text())
    assert version_code>=40 and native['passed'] and native['persistedPlusTen'] and portable['passed']
    assert portable['serverCoreSha256']==core
    assert native['definitionSha256']==sha(ROOT/'Project/Assets/Scripts/Assembly-CSharp/LocalHelperCostumeDefinition.cs')
    assert native['iconSha256']==sha(ROOT/'Project/Assets/Resources/helper/LocalNightSakura/icons.png')
    with zipfile.ZipFile(APK) as outer,zipfile.ZipFile(io.BytesIO(outer.read('assets/embedded-server.zip'))) as content:
        assert hashlib.sha256(content.read('LocalServer/data/activities.json')).hexdigest()==native['activityConfigSha256']==portable['activityConfigSha256']
    result.update(helperCostumeVerified=True,helperCostumeMaxEnchant=10,helperCostumeSharedByThree=True,
        helperCostumeDefinitionSha256=native['definitionSha256'],helperCostumeIconSha256=native['iconSha256'],
        helperCostumeAndroidDeviceVerified=False)
if options.require_adventure_drop:
    drop=json.loads((ROOT/'Verification/adventure-drop-portable-latest.json').read_text('utf8'))
    assert version_code>=43 and drop['passed'] and drop['dropRule']=='probability' and not drop['productionSaveUsed'] and drop['serverCoreSha256']==core
    with zipfile.ZipFile(APK) as outer,zipfile.ZipFile(io.BytesIO(outer.read('assets/embedded-server.zip'))) as content:
        attendance=content.read('LocalServer/data/attendance.json')
    assert hashlib.sha256(attendance).hexdigest()==drop['attendanceConfigSha256']==sha(ROOT/'WindowsPreview/LocalServer/data/attendance.json')
    operations=json.loads(attendance)['operations']['items']
    assert all(next(x['value'] for x in operations if x['type']==mode+'_BATTLE_ITEM_DROP')==150 for mode in ['NORMAL','HARD','LEGEND'])
    result.update(adventureDropVerified=True,adventureDropChanceMultiplier=150,adventureDropDeviceVerified=False)
if options.require_story:
    story=json.loads((ROOT/'Verification/story-protocol-latest.json').read_text('utf8'))
    assert version_code>=52 and story['passed'] and story['storyChecks']>=86 and story['coreSha256']['android']==core
    result.update(storyChapterVerified=True,storyMapCompiled=True,storyFullPageCompiled=True,hongGrowthCompiled=True,cardRegistryCompiled=True,storyCatalogSha256=hashlib.sha256(story_catalog).hexdigest(),storyProtocolChecks=story['storyChecks'],gachaChecks=story['gachaChecks'],storyDeviceVerified=False)
if options.require_shop_edges:
    shop=json.loads((ROOT/'Verification/shop-edges-native-latest.json').read_text())
    build=json.loads((ROOT/'Verification/shop-edge-android-build-inputs.json').read_text())
    source=sha(ROOT/'Project/Assets/Scripts/Assembly-CSharp/LocalShopNativeUI.cs')
    assert shop['passed'] and shop['errors']==0 and not shop['overlapReproduced'] and shop['checks']['bossShopGuttersUnchanged']
    assert build['passed'] and build['sourceSha256']==shop['sourceSha256']==source
    assert build['apkSha256']==sha(APK) and build['versionCode']==version_code
    result.update(sharedShopEdgesVerified=True,shopLayoutSourceSha256=source,shopNativeChecks=len(shop['checks']),
        shopEdgeAndroidDeviceVerified=False,shopEdgeVerificationScope='Shared compiled layout and desktop native geometry/input; Android device not connected')
if options.require_export_performance:
    assert version_code>=39
    performance=json.loads((ROOT/'Verification/android-manager-contract/export-performance.json').read_text())
    baseline=json.loads((ROOT/'Verification/android-manager-contract/export-performance-baseline.json').read_text())
    build=json.loads((ROOT/'Verification/android-export-performance-build.json').read_text())
    assert performance['passed'] and performance['checks']>=42 and not performance['phoneUsed']
    assert performance['documentBytes']==baseline['documentBytes'] and performance['logBytes']==baseline['logBytes']
    assert build['passed'] and build['apkSha256']==sha(APK) and build['versionCode']==version_code
    for group in [performance['sourceSha256'],build['sourceSha256']]:
        for name,want in group.items():assert sha(ROOT/name)==want,name
    update=adapter[adapter.index('private void Update()'):adapter.index('private void OnApplicationPause')]
    assert 'bundleExport' not in update and 'AndroidJNI.AttachCurrentThread()' in export_command and '"bundleExport"' in export_command
    assert console_export['checks']>=13
    result.update(exportPerformanceVerified=True,exportPerformanceChecks=performance['checks'],
        exportPerformanceSourceSha256=build['sourceSha256'],workerExportHandoffSourceAndBuildVerified=True,
        exportTimingsIncluded=True,storedNestedSnapshot=True,exportPerformanceDeviceVerified=False,
        desktopSyntheticExportComparison=dict(jsonBytes=performance['documentBytes'],logBytes=performance['logBytes'],
            beforeBundleMs=baseline['bundleMs'],afterBundleMs=performance['bundleMs'],
            beforeValidationMs=baseline['validationMs'],afterValidationMs=performance['validationMs'],
            beforeBundleBytes=baseline['bundleBytes'],afterBundleBytes=performance['bundleBytes']))
OUT.mkdir(exist_ok=True)
(OUT/'apk-inspection.json').write_text(json.dumps(result,indent=2))
(OUT/'signature.txt').write_text(signature)
(OUT/'badging.txt').write_text(badging,encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k!='nativeLibraries'},indent=2))
