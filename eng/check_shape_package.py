from pathlib import Path
from tempfile import TemporaryDirectory
from zipfile import ZipFile
import json
import subprocess
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
feed = root / "artifacts/shape-packages"
smoke = root / "artifacts/shape-smoke"
reference = root / "artifacts/nuget/netstandard.library.ref/2.1.0"
feed.mkdir(parents=True, exist_ok=True)
smoke.mkdir(parents=True, exist_ok=True)
(root / "artifacts/tmp").mkdir(parents=True, exist_ok=True)
if not reference.is_dir():
    raise SystemExit("Run eng/dotnet_test.sh tests/SA.Shape.Tests/SA.Shape.Tests.csproj first to restore the SDK reference pack")

versions = {}
for name in ["SA.Motion", "SA.Animation", "SA.Shape"]:
    project = root / "src" / name / (name + ".csproj")
    versions[name] = ET.parse(project).findtext("PropertyGroup/Version")
    subprocess.run(["dotnet", "pack", str(project), "--configuration", "Release", "--no-build", "--no-restore", "--disable-build-servers", "-o", str(feed)], cwd=root, check=True)
    with ZipFile(feed / f"{name}.{versions[name]}.nupkg") as package:
        for framework in ["net10.0", "netstandard2.1"]:
            assert f"lib/{framework}/{name}.dll" in package.namelist()
        if name == "SA.Shape":
            assert "README.md" in package.namelist()
            spec = ET.fromstring(package.read(name + ".nuspec"))
            dependencies = [(x.attrib["id"], x.attrib["version"]) for x in spec.iter() if x.tag.endswith("dependency")]
            assert dependencies == [("SA.Animation", versions["SA.Animation"])] * 2

project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
properties = ET.SubElement(project, "PropertyGroup")
ET.SubElement(properties, "TargetFrameworks").text = "net10.0;netstandard2.1"
ET.SubElement(properties, "LangVersion").text = "9.0"
ET.SubElement(properties, "OutputType", Condition="'$(TargetFramework)' == 'net10.0'").text = "Exe"
ET.SubElement(properties, "TreatWarningsAsErrors").text = "true"
ET.SubElement(ET.SubElement(project, "ItemGroup"), "PackageReference", Include="SA.Shape", Version=f"[{versions['SA.Shape']}]")
(smoke / "ShapeSmoke.csproj").write_text(ET.tostring(project, encoding="unicode"))
(smoke / "Program.cs").write_text('''using System;
using SA.Animation;
using SA.Motion.Numerics;
using SA.Shape;
public static class Program
{
    public static int Main()
    {
        var family = new AssetId(1, 0, 0, 0);
        var target = new AssetId(2, 0, 0, 0);
        var definitionId = new AssetId(3, 0, 0, 0);
        var parents = new int[] { -1, 0, 1 };
        var rest = new Pose[] { Pose.Identity, new Pose { Position = new Float3(0, 1, 0), Rotation = Quaternion.Identity, Scale = Float3.One }, new Pose { Position = new Float3(1, 0, 0), Rotation = Quaternion.Identity, Scale = Float3.One } };
        var rows = new RetargetRow[3];
        var error = RetargetTable.Compile(family, target, parents, rest, parents, rest, new int[] { 0, 1, 2 }, -1, -1, 1, new RigidPose[6], rows, out var binding);
        if (error != AnimationError.None) return 1;
        var channels = new ShapeChannel[] { new ShapeChannel(1, -1, 1, 0, 0) };
        var state = new ShapeParameter[1];
        if (ShapeParameters.Reset(channels, state) != AnimationError.None || ShapeParameters.Step(channels, new float[] { 1 }, state, .01f, state) != AnimationError.None) return 2;
        var definition = new ShapeDefinition(new ShapeProfile(definitionId, target, default, default, ShapeGrounding.Free), channels,
            new OffsetEffect[] { new OffsetEffect(0, 2, Float3.Zero, (float)Math.Log(2)) }, new SkinEffect[] { new SkinEffect(0, 1, new Float3((float)Math.Log(2), 0, 0)) }, ReadOnlySpan<SkinFrame>.Empty);
        var offsets = new Float3[3]; var lengths = new float[3]; var scales = new float[3]; var skin = new Float3[3]; var deformations = new Affine[3]; var models = new RigidPose[3];
        error = ShapeRig.Evaluate(definition, binding, rows, new float[] { state[0].Value }, 1, new ShapeWorkspace(offsets, lengths, scales, skin, deformations, models), out var geometry);
        if (error != AnimationError.None || offsets[2].X != 2f) return 3;
        var posed = new Pose[3];
        error = RetargetTable.ApplyGeometry(family, binding, geometry.Retarget, parents, rest, rows, offsets, new RigidPose[3], models, posed);
        if (error != AnimationError.None || posed[2].Position.X != 2f || posed[2].Scale.X != 1f) return 4;
        Console.WriteLine("SA.Shape package smoke passed; animation=" + typeof(RetargetTable).Assembly.GetName().Version + "; motion=" + typeof(Float3).Assembly.GetName().Version);
        return 0;
    }
}
''')

with TemporaryDirectory(prefix="shape-package-cache-", dir=root / "artifacts/tmp") as cache:
    config = ET.Element("configuration")
    sources = ET.SubElement(config, "packageSources")
    ET.SubElement(sources, "clear")
    ET.SubElement(sources, "add", key="shape-local", value=str(feed))
    ET.SubElement(sources, "add", key="sdk-ref-cache", value=str(reference.resolve()))
    ET.SubElement(ET.SubElement(config, "config"), "add", key="globalPackagesFolder", value=cache)
    (smoke / "NuGet.Config").write_text(ET.tostring(config, encoding="unicode"))
    subprocess.run(["dotnet", "restore", str(smoke / "ShapeSmoke.csproj"), "--configfile", str(smoke / "NuGet.Config"), "--force"], cwd=root, check=True)
    subprocess.run(["dotnet", "build", str(smoke / "ShapeSmoke.csproj"), "--configuration", "Release", "--no-restore", "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false"], cwd=root, check=True)
    assets = json.loads((smoke / "obj/project.assets.json").read_text())
    for framework, target in assets["targets"].items():
        expected = "netstandard2.1" if "standard" in framework.lower() else "net10.0"
        for name, version in versions.items():
            assert all(f"/{expected}/" in asset for asset in target[f"{name}/{version}"]["compile"])
    subprocess.run(["dotnet", str(smoke / "bin/Release/net10.0/ShapeSmoke.dll")], cwd=root, check=True)
print("shape package check: both target assets, isolated local-feed restore/build and executable consumer passed")
