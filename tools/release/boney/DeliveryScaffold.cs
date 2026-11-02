using System.Text.Json;
using System.Text.Json.Nodes;

namespace Boney.Delivery;

public static class DeliveryScaffold
{
    private static string BootstrapScript()
    {
        var inventory = string.Join(", ", DeliverySnapshot.RuntimeFiles.Select(name => "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'"));
        return $$"""
        $ErrorActionPreference = 'Stop'
        Set-StrictMode -Version Latest
        function Require-Directory([string] $path) {
          $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
          if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'DELIVERY_SNAPSHOT_INVALID' }
        }
        function Read-Regular([string] $path) {
          $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
          if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'DELIVERY_SNAPSHOT_INVALID' }
          return ,([IO.File]::ReadAllBytes($item.FullName))
        }
        function Require-Keys($value, [string[]] $expected) {
          if ($value.ValueKind -ne [Text.Json.JsonValueKind]::Object) { throw 'DELIVERY_SNAPSHOT_INVALID' }
          $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
          foreach ($property in $value.EnumerateObject()) {
            if (-not $seen.Add($property.Name) -or $expected -cnotcontains $property.Name) { throw 'DELIVERY_SNAPSHOT_INVALID' }
          }
          if ($seen.Count -ne $expected.Count) { throw 'DELIVERY_SNAPSHOT_INVALID' }
        }
        try {
          $names = @({{inventory}})
          $source = $env:GITHUB_WORKSPACE
          Require-Directory $source
          foreach ($part in @('tools', 'release', 'boney')) { $source = Join-Path $source $part; Require-Directory $source }
          [byte[]] $lockBytes = Read-Regular (Join-Path $source 'boney.lock.json')
          $utf8 = [Text.UTF8Encoding]::new($false, $true)
          $document = [Text.Json.JsonDocument]::Parse($utf8.GetString($lockBytes))
          try {
            $manifest = $document.RootElement
            Require-Keys $manifest @('schemaVersion', 'sourceRepository', 'sourceRevision', 'files')
            if ($manifest.GetProperty('schemaVersion').GetRawText() -cne '1' -or $manifest.GetProperty('sourceRepository').GetString() -cne 'TBD-APS/boney' -or $manifest.GetProperty('sourceRevision').GetString() -cnotmatch '\A[a-f0-9]{40}\z') { throw 'DELIVERY_SNAPSHOT_INVALID' }
            $hashes = $manifest.GetProperty('files')
            Require-Keys $hashes $names
            $expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($name in $names + @('boney.lock.json')) { [void] $expected.Add($name) }
            $found = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            foreach ($entry in Get-ChildItem -LiteralPath $source -Force) {
              if ($expected.Contains($entry.Name)) { [void] $found.Add($entry.Name) }
              elseif ($entry.Name -ceq 'bin' -or $entry.Name -ceq 'obj') { Require-Directory $entry.FullName }
              else { throw 'DELIVERY_SNAPSHOT_INVALID' }
            }
            if ($found.Count -ne $expected.Count) { throw 'DELIVERY_SNAPSHOT_INVALID' }
            $buffers = [Collections.Generic.Dictionary[string, byte[]]]::new([StringComparer]::Ordinal)
            foreach ($name in $names) {
              $digest = $hashes.GetProperty($name).GetString()
              if ($digest -cnotmatch '\A[a-f0-9]{64}\z') { throw 'DELIVERY_SNAPSHOT_INVALID' }
              [byte[]] $bytes = Read-Regular (Join-Path $source $name)
              $actual = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
              if ($actual -cne $digest) { throw 'DELIVERY_SNAPSHOT_INVALID' }
              $buffers.Add($name, $bytes)
            }
            $buffers.Add('boney.lock.json', $lockBytes)
          } finally { $document.Dispose() }
          Require-Directory $env:RUNNER_TEMP
          $copy = Join-Path $env:RUNNER_TEMP ('boney-delivery-' + [Guid]::NewGuid().ToString('N'))
          if (Test-Path -LiteralPath $copy) { throw 'DELIVERY_SNAPSHOT_INVALID' }
          [void] [IO.Directory]::CreateDirectory($copy)
          Require-Directory $copy
          foreach ($name in $buffers.Keys) {
            $stream = [IO.File]::Open((Join-Path $copy $name), [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
            try { $stream.Write($buffers[$name], 0, $buffers[$name].Length) } finally { $stream.Dispose() }
          }
          $config = Join-Path $env:RUNNER_TEMP ('boney-nuget-' + [Guid]::NewGuid().ToString('N') + '.config')
          $configBytes = [Text.Encoding]::UTF8.GetBytes('<configuration><packageSources><clear /></packageSources></configuration>')
          $stream = [IO.File]::Open($config, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
          try { $stream.Write($configBytes, 0, $configBytes.Length) } finally { $stream.Dispose() }
          Push-Location -LiteralPath $copy
          try {
            & dotnet build Boney.Delivery.csproj --configuration Release --nologo -p:ImportDirectoryBuildProps=false -p:ImportDirectoryBuildTargets=false -p:ImportDirectoryPackagesProps=false "-p:RestoreConfigFile=$config" -p:NuGetAudit=false
            if ($LASTEXITCODE -ne 0) { throw 'DELIVERY_BUILD_FAILED' }
          } finally { Pop-Location }
          $dll = Join-Path $copy 'bin/Release/net10.0/Boney.Delivery.dll'
          [void] (Read-Regular $dll)
          [IO.File]::AppendAllText($env:GITHUB_ENV, ('BONEY_DELIVERY_DLL=' + $dll + "`n"), [Text.UTF8Encoding]::new($false))
        } catch { throw 'DELIVERY_BOOTSTRAP_REFUSED' }
        """;
    }

    public static void Create(string directory, JsonObject input, IReadOnlyList<IReadOnlyList<string>> validation)
    {
        var profile = DeliveryProfile.Validate(input);
        if (profile["environments"]?["production"] is not JsonObject target || !((JsonArray)profile["events"]!).Any(e => e!.GetValue<string>() == "workflow_dispatch")
            || profile["ci"].Text("workflow") != "ci.yml" || profile["ci"]?["requiredJobs"] is not JsonArray { Count: 1 } jobs || jobs[0]!.GetValue<string>() != "CI Gate"
            || validation.Count == 0 || validation.Any(argv => argv.Count == 0 || argv.Any(arg => string.IsNullOrEmpty(arg) || arg.Any(c => c is '\r' or '\n' or '\0')))) throw Json.Error("DELIVERY_SCAFFOLD_INVALID");
        static string Quote(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
        var workflow = Path.Combine(directory, ".github/workflows"); Directory.CreateDirectory(workflow);
        var setup = $$"""
              - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9
                with:
                  dotnet-version: '10.0.x'
              - name: Verify and build the pinned Boney baseline
                shell: pwsh
                run: |
        {{string.Join('\n', BootstrapScript().Split('\n').Select(line => "          " + line))}}
              - name: Verify the pinned Boney baseline
                run: dotnet "$BONEY_DELIVERY_DLL" verify-snapshot tools/release/boney
        """;
        var ci = $$$"""
        name: CI
        on:
          pull_request:
          push:
            branches: [{{{JsonSerializer.Serialize(profile.Text("branch"))}}}]
          workflow_dispatch:
        permissions:
          contents: read
        jobs:
          gate:
            name: CI Gate
            runs-on: ubuntu-latest
            timeout-minutes: 30
            steps:
              - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262
                with:
                  persist-credentials: false
        {{{setup}}}
              - uses: actions/setup-node@49933ea5288caeca8642d1e84afbd3f7d6820020
                with:
                  node-version: '22'
              - name: Product validation
                run: |
                  set -Eeuo pipefail
        {{{string.Join('\n', validation.Select(argv => "          " + string.Join(' ', argv.Select(Quote))))}}}
        """;
        var release = $$$"""
        name: Product release
        on:
          workflow_dispatch:
        permissions:
          contents: read
          pull-requests: read
          actions: read
        concurrency:
          group: product-release-production
          cancel-in-progress: false
        jobs:
          release:
            runs-on: ubuntu-latest
            timeout-minutes: 60
            environment: {{{JsonSerializer.Serialize(target.Text("githubEnvironment"))}}}
            steps:
              - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262
                with:
                  ref: ${{ github.sha }}
                  persist-credentials: false
        {{{setup}}}
              - name: Run the C# product adapter with a retained release-store volume
                env:
                  GITHUB_TOKEN: ${{ github.token }}
                  BONEY_RELEASE_STORE: ${{ vars.BONEY_RELEASE_STORE }}
                run: >-
                  dotnet "$BONEY_DELIVERY_DLL" run
                  --profile product.delivery.json --adapter tools/release/product-adapter.cs
                  --environment production --revision "$GITHUB_SHA"
                  --store "$BONEY_RELEASE_STORE" --receipt .evidence/delivery/release-result.json
              - uses: actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9
                if: always()
                with:
                  name: release-evidence-${{ github.run_id }}-${{ github.run_attempt }}
                  path: .evidence/delivery/
                  include-hidden-files: true
                  if-no-files-found: error
        """;
        foreach (var (name, bytes) in new[] { ("product.delivery.json", profile.ToJsonString(Json.Pretty)), (".github/workflows/ci.yml", ci), (".github/workflows/release.yml", release) })
        {
            using var stream = new StreamWriter(new FileStream(Path.Combine(directory, name), FileMode.CreateNew)); stream.WriteLine(bytes);
        }
    }
}
