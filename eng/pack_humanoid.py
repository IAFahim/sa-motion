import hashlib
import json
import os
import shutil
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parents[1]
artifacts = root / "artifacts"
(artifacts / "tmp").mkdir(parents=True, exist_ok=True)
env = dict(os.environ, TMPDIR=str(artifacts / "tmp"), DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER="1")
subprocess.run(["dotnet", "build", "src/SA.Humanoid/SA.Humanoid.csproj", "--configuration", "Release", "--no-restore", "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false"], cwd=root, env=env, check=True, stdout=subprocess.DEVNULL)
built = root / "src/SA.Humanoid/bin/Release/netstandard2.1/SA.Humanoid.dll"
plugins = root / "packages/com.sa.unity.reactions/Runtime/Plugins"
plugins.mkdir(parents=True, exist_ok=True)
shutil.copyfile(built, plugins / "SA.Humanoid.dll")
sha = hashlib.sha256(built.read_bytes()).hexdigest()
(root / "packages/com.sa.unity.reactions/artifact.json").write_text(json.dumps({"assembly": "SA.Humanoid.dll", "framework": "netstandard2.1", "sha256": sha}, indent=2) + "\n")
print(json.dumps({"sha256": sha}))
