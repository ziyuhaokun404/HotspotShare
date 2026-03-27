param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$toolDir = Join-Path $env:TEMP "hotspotshare-icon-tool"
New-Item -ItemType Directory -Force -Path $toolDir | Out-Null

Push-Location $toolDir

try {
    if (-not (Test-Path "package.json")) {
        npm init -y | Out-Null
    }

    if (-not (Test-Path "node_modules/@resvg/resvg-js") -or -not (Test-Path "node_modules/png-to-ico")) {
        npm install @resvg/resvg-js@2.6.2 png-to-ico@3.0.1 | Out-Null
    }

    $env:PROJECT_DIR = $ProjectRoot
    $script = @'
const fs = require('node:fs');
const path = require('node:path');
const { Resvg } = require('@resvg/resvg-js');
const pngToIco = require('png-to-ico').default;

(async () => {
  const projectDir = process.env.PROJECT_DIR;
  const svgPath = path.join(projectDir, 'assets', 'icon.svg');
  const outDir = path.join(projectDir, 'src', 'Assets');

  fs.mkdirSync(outDir, { recursive: true });

  const svg = fs.readFileSync(svgPath);
  const pngPath = path.join(outDir, 'icon.png');
  const icoPath = path.join(outDir, 'icon.ico');

  const png = new Resvg(svg, {
    fitTo: { mode: 'width', value: 512 },
    background: 'rgba(0,0,0,0)'
  }).render().asPng();
  fs.writeFileSync(pngPath, png);

  const sizes = [16, 24, 32, 48, 64, 128, 256];
  const buffers = sizes.map(size => new Resvg(svg, {
    fitTo: { mode: 'width', value: size },
    background: 'rgba(0,0,0,0)'
  }).render().asPng());

  const ico = await pngToIco(buffers);
  fs.writeFileSync(icoPath, ico);

  console.log(`Generated ${pngPath}`);
  console.log(`Generated ${icoPath}`);
})();
'@

    node -e $script
}
finally {
    Pop-Location
}
