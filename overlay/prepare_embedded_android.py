"""Build the Android Unity host from the authoritative server sources and isolated original model."""
from pathlib import Path
import hashlib, json, shutil, subprocess, sys
from zipfile import ZipFile
import io

ROOT = Path(__file__).resolve().parent
HOST = ROOT / 'ServerBuildSource/UnityHost'
RESEARCH = ROOT / 'Research/AndroidInProcess'

def write(path, text):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8', newline='\n' if path.suffix=='.meta' else None)

def run(args):
    subprocess.run(args, cwd=ROOT, check=True, creationflags=subprocess.CREATE_NO_WINDOW)

def main():
    # Proven API adaptations only; no second copy of gameplay business logic is maintained.
    run([sys.executable, str(RESEARCH/'prepare_portable.py')])
    for name in ['PortableApis.cs', 'CompilerAttributes.cs']:
        shutil.copyfile(RESEARCH/'Compatibility'/name, HOST/name)
    isolation = HOST/'obj/AssemblyIsolation'
    isolation.mkdir(parents=True, exist_ok=True)
    for source in (RESEARCH/'AssemblyIsolation').glob('*.cs*'):
        text = source.read_text(encoding='utf-8-sig')
        text = text.replace('Research/AndroidInProcess/Compatibility/obj/Isolated', 'ServerBuildSource/UnityHost/obj/Isolated')
        text = text.replace('Research/AndroidInProcess/Compatibility/bin/Release/netstandard2.1/Bnsm.ServerCore.UnityResearch.dll', 'ServerBuildSource/UnityHost/bin/Release/netstandard2.1/Bnsm.EmbeddedServer.dll')
        text = text.replace('Bnsm.ServerCore.UnityResearch', 'Bnsm.EmbeddedServer')
        # Nested protobuf/data types are roots too. Preserve schema methods needed by reflection.
        text = text.replace('t.Namespace is "BscTable" or "USER_PACKET"', '(t.Namespace is "BscTable" or "USER_PACKET" || t.FullName.StartsWith("BscTable.") || t.FullName.StartsWith("USER_PACKET.") || t.CustomAttributes.Any(a=>a.AttributeType.FullName=="ProtoBuf.ProtoContractAttribute"))')
        # All retained types are safe to preserve after the model closure has removed updater/UI code.
        text = text.replace('new XAttribute("fullname","Bnsm.ServerGameModel"));', 'new XAttribute("fullname","Bnsm.ServerGameModel"),new XAttribute("preserve","all"));')
        write(isolation/source.name, text)
    project = (RESEARCH/'Compatibility/UnityPortable.csproj').read_text(encoding='utf-8-sig')
    project = project.replace('<Root>$(MSBuildProjectDirectory)/../../..</Root>', '<Root>$(MSBuildProjectDirectory)/../..</Root>')
    project = project.replace('Bnsm.ServerCore.UnityResearch', 'Bnsm.EmbeddedServer').replace(';BNSM_UNITY_RESEARCH', ';BNSM_UNITY_EMBEDDED')
    project = project.replace('obj/PortableSources/*.cs', '../../Research/AndroidInProcess/Compatibility/obj/PortableSources/*.cs')
    project = project.replace('<ItemGroup>', '<ItemGroup>\n    <InternalsVisibleTo Include="Bnsm.LocalServer" />', 1)
    write(HOST/'UnityHost.csproj', project)
    http = (ROOT/'ServerBuildSource/AndroidHost/PhoneDebugHttpHost.cs').read_text(encoding='utf-8-sig')
    http = http.replace('namespace Bnsm.AndroidHost;', 'namespace Bnsm.UnityHost;').replace('PhoneDebugHttpHost', 'EmbeddedHttpHost').replace('58601', '58611')
    http = http.replace('PhoneSupportInfo.Snapshot(Android.App.Application.Context)', 'Host.Snapshot()')
    http = http.replace('Android.Util.Log.Error("BnsmLocalHost","Debug request: "+error.GetType().Name)', 'ServerDiagnostics.Error("Debug request: "+error.GetType().Name)')
    write(HOST/'EmbeddedHttpHost.cs', http)
    command = ['dotnet','run','--project',str(isolation/'AssemblyIsolation.csproj'),'-c','Release','--',str(ROOT)]
    run(command+['--bootstrap'])
    run(['dotnet','build',str(HOST/'UnityHost.csproj'),'-c','Release','--nologo','-v','quiet'])
    run(command)
    # Rebuild so the output folder contains the final pruned model rather than the bootstrap copy.
    run(['dotnet','build',str(HOST/'UnityHost.csproj'),'-c','Release','--no-restore','--nologo','-v','quiet'])
    plugins = ROOT/'Project/Assets/Plugins/EmbeddedServer'; plugins.mkdir(parents=True, exist_ok=True)
    build = HOST/'bin/Release/netstandard2.1'
    libraries = [build/n for n in ['Bnsm.EmbeddedServer.dll','Bnsm.ServerGameModel.dll','Bnsm.ServerLegacyEngine.dll']]
    manifest = json.loads((HOST/'obj/project.assets.json').read_text())
    for package, values in manifest['targets']['netstandard2.1'].items():
        for relative in values.get('runtime', {}):
            if relative.endswith('.dll'):
                libraries.append(Path.home()/'.nuget/packages'/manifest['libraries'][package]['path']/relative)
    proof = []
    for item in libraries:
        target = plugins/item.name; shutil.copyfile(item,target)
        explicit = 0 if item.name=='Bnsm.EmbeddedServer.dll' else 1
        guid = hashlib.sha256(('BnsmEmbedded/'+item.name).encode()).hexdigest()[:32]
        write(Path(str(target)+'.meta'), f'''fileFormatVersion: 2
guid: {guid}
PluginImporter:
  externalObjects: {{}}
  serializedVersion: 2
  iconMap: {{}}
  executionOrder: {{}}
  defineConstraints: []
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: {explicit}
  validateReferences: 0
  platformData:
  - first:
      Any:
    second:
      enabled: 0
      settings: {{}}
  - first:
      Android: Android
    second:
      enabled: 1
      settings: {{}}
  - first:
      iPhone: iOS
    second:
      enabled: 1
      settings: {{}}
  userData:
  assetBundleName:
  assetBundleVariant:
''')
        proof.append(dict(file=item.name,sha256=hashlib.sha256(target.read_bytes()).hexdigest()))
    shutil.copyfile(HOST/'obj/Isolated/link.xml', plugins/'link.xml')
    archive = ROOT/'Project/Assets/StreamingAssets/embedded-server.zip'
    with ZipFile(ROOT/'Builds/PhoneServer/apk/local.bnsm.serverhost-Signed.apk') as apk:
        original_content=apk.read('assets/server-content.zip')
    # The retained host APK can predate a new additive activity. Keep its exact
    # original catalogs, but package the verified current local configuration.
    current_content=(ROOT/'Builds/PhoneServer/server-content.zip').read_bytes()
    with ZipFile(io.BytesIO(original_content)) as previous,ZipFile(io.BytesIO(current_content)) as current:
        assert current.testzip() is None and previous.namelist()==current.namelist()
        changed=[name for name in previous.namelist() if previous.read(name)!=current.read(name)]
        assert set(changed)<= {'LocalServer/data/activities.json'},'Unexpected original catalog change: '+str(changed)
        assert current.read('LocalServer/data/activities.json')==(ROOT/'WindowsPreview/LocalServer/data/activities.json').read_bytes()
    archive.write_bytes(current_content)
    adapters = list(HOST.glob('*.cs')) + [Path(__file__).resolve(), RESEARCH/'prepare_portable.py', RESEARCH/'Compatibility/UnityPortable.csproj']
    adapters += list((RESEARCH/'AssemblyIsolation').glob('*.cs'))
    for name in ['LocalAdventureDropRules', 'LocalAdventureChapter53', 'LocalCardRegistry', 'LocalHongCard', 'LocalHongEquipment', 'LocalHongRelations', 'LocalStoryChapter']:
        adapters.append(ROOT/f'Project/Assets/Scripts/Assembly-CSharp/{name}.cs')
    adapters += [RESEARCH/'Compatibility/PortableApis.cs', RESEARCH/'Compatibility/CompilerAttributes.cs']
    adapters.append(ROOT/'Project/Assets/Resources/LocalStoryCatalog.json')
    report = dict(libraries=proof,contentSha256=hashlib.sha256(archive.read_bytes()).hexdigest(),
                  sourceManifest=json.loads((RESEARCH/'Compatibility/obj/PortableSources/sources.json').read_text()) +
                  [dict(source=str(p.relative_to(ROOT)),sha256=hashlib.sha256(p.read_bytes()).hexdigest()) for p in adapters])
    write(ROOT/'Verification/android-embedded-inputs.json',json.dumps(report,indent=2))
    print('Embedded Android inputs ready')

if __name__=='__main__': main()
