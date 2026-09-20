<#
.SYNOPSIS
    Entrega de sOC Lucia: version en el csproj, exe autocontenido + MSIX + .vsix, OneDrive, commit, push y release.
.DESCRIPTION
    El CHANGELOG se escribe antes a mano (la primera seccion son las notas de la release).
    Publica: sin marcha atras. La copia en OneDrive es para Josep; la release es lo que queda.
.EXAMPLE
    .\tools\entregar.ps1 -Version 2026.9.20.0 -Mensaje "Primera version"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string] $Mensaje
)
$ErrorActionPreference = 'Stop'
$raiz = Split-Path -Parent $PSScriptRoot
Set-Location $raiz
[IO.Directory]::SetCurrentDirectory($raiz)

# 1. Version.
$p = 'sOCLucia.csproj'; $x = Get-Content $p -Raw
foreach ($t in 'Version', 'AssemblyVersion', 'FileVersion') { $x = [regex]::Replace($x, "<$t>[^<]*</$t>", "<$t>$Version</$t>") }
[IO.File]::WriteAllText($p, $x)
$parts = $Version.Split('.'); $msixVer = "$($parts[0]).$($parts[1]).$($parts[2])$($parts[3]).0"
$semver = "$($parts[0]).$($parts[1]).$($parts[2])$(if ($parts[3] -ne '0') { $parts[3] })"
$v = Get-Content 'vscode\package.json' -Raw
$v = [regex]::Replace($v, '"version": "[^"]+"', "`"version`": `"$semver`"", 1)
[IO.File]::WriteAllText('vscode\package.json', $v)

# 2. Exe + MSIX (el script de MSIX publica el exe autocontenido de un solo fichero).
Get-Process sOCLucia -ErrorAction SilentlyContinue | Stop-Process -Force
.\tools\empaquetar-msix.ps1 2>&1 | Select-String "Paquete:|error|fall"
$exe = 'bin\Release\net10.0-windows\win-x64\publish\sOCLucia.exe'
if (-not (Test-Path $exe)) { throw 'No hay exe publicado.' }

# 3. Extension de VS Code.
Push-Location vscode
Remove-Item *.vsix -ErrorAction SilentlyContinue
npx --yes @vscode/vsce package --no-dependencies 2>&1 | Select-Object -Last 1
$vsix = Get-ChildItem *.vsix | Select-Object -First 1
Pop-Location
if (-not $vsix) { throw 'No hay .vsix.' }

# 4. OneDrive (constitucion general, 8).
$d = 'C:\ID\OneDrive\Lucia'
New-Item -ItemType Directory -Force $d | Out-Null
Get-ChildItem $d -Include *.msix, *.vsix -Recurse | Remove-Item -Force
Copy-Item $exe $d -Force
Copy-Item bin\sOCLucia.msix "$d\sOCLucia-$msixVer.msix" -Force
Copy-Item $vsix.FullName $d -Force
$leeme = @"
sOC Lucia para Windows
========================

Version $Version

Que es
------
Una IA privada que se ejecuta en este PC: un modelo de lenguaje local con el que hablas desde una
ventana de chat. Nada de lo que escribes sale del ordenador. Con una puerta local para editores de
codigo, VS Code puede programar con esa misma IA.

Antes se llamaba sOC AI Chat
----------------------------
Hasta la 2026.9.20.3 el exe era sOCAIChat.exe. Cierralo si lo tienes abierto y abre sOCLucia.exe:
tus conversaciones, ajustes, motor e IA se trasladan solos la primera vez.

Como ejecutarlo
---------------
Basta con sOCLucia.exe: copialo donde quieras y abrelo. La primera vez pide elegir una IA (la
descarga de Hugging Face, del tamaño de tu memoria) o importar un fichero GGUF que ya tengas, y
baja el motor llama.cpp (unos 30 MB; con NVIDIA, unos 600 MB de CUDA). Todo va a
%LOCALAPPDATA%\sOCLucia. Windows puede avisar de que el editor es desconocido ("Windows protegio
su PC"): pulsa "Mas informacion" y "Ejecutar de todas formas".

VS Code
-------
$($vsix.Name): la extension "sOC Lucia Code". Instalar: Extensiones > ... > Instalar desde VSIX.
Luego, en sOC Lucia > Ajustes > Editores de codigo (VS Code): activar y copiar el token en la
opcion socLucia.token de VS Code. Ctrl+Alt+L abre el chat (Ctrl+Alt+I lo usa Copilot); boton derecho sobre una seleccion >
sOC Lucia. Sirve tambien Continue, Cline o cualquier cliente compatible con OpenAI, con la
direccion http://127.0.0.1:41417/v1 y ese token.

El fichero .msix
----------------
Es el paquete de instalacion; sin firmar, Windows no lo instala (sirve para la Store si algun dia
se envia). Para usar la aplicacion basta con el exe.

Software libre bajo licencia MIT. En español y en ingles, claro y oscuro.
"@
[IO.File]::WriteAllText("$d\LEEME.txt", $leeme, (New-Object Text.UTF8Encoding $false))
"OneDrive: " + ((Get-ChildItem $d | ForEach-Object { $_.Name }) -join ', ')

# 5. Git y release.
git add -A
git commit -q -m $Mensaje
git push -q origin HEAD 2>&1 | Select-Object -Last 1
python 'D:\sOCProjects\Mobile\Shared\release-github.py' "v$Version" $exe bin\sOCLucia.msix $vsix.FullName 2>&1 | Select-Object -Last 1
