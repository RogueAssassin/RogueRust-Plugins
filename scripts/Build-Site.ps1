#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds the plugin website (https://plugins.roguegaming.com.au) into ./_site.

.DESCRIPTION
    The site is generated from what is already in the repository, so it cannot drift from it:
      - plugins/*.cs      -> the catalogue (name and version come from each [Info] attribute)
      - README.md         -> the one-line description beside each plugin
      - docs/*.md         -> one page per plugin
      - site/style.css    -> the look
    Run it locally with:  pwsh scripts/Build-Site.ps1   then open _site/index.html.
    Requires PowerShell 7 (ConvertFrom-Markdown).
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [string]$Domain = 'plugins.roguegaming.com.au'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root '_site' }

$extensionUrl = 'https://github.com/RogueAssassin/Oxide.Ext.RogueRust/releases'
$repositoryUrl = 'https://github.com/RogueAssassin/RogueRust-Plugins'

# Where each plugin sits on the home page. A plugin that is not listed lands in "More".
$categories = [ordered]@{
    'Administration' = @('RogueRustAdminMenu', 'RogueRustServerInfo', 'RogueRustServerRestarter', 'RogueRustCustomPlayerMessages')
    'World events'   = @('RogueRustEventDirector', 'RogueRustHeliCrash', 'RogueRustHeliLeashControl', 'RogueRustHackableCrateTracker', 'RogueRustDeathNotes')
    'Building and base' = @('RogueRustBuildingManager', 'RogueRustRemovalTool', 'RogueRustGridPower', 'RogueRustTurretLimitOverride', 'RogueRustFurnaceSplitter', 'RogueRustFuelStackOptimizer')
    'Players and progression' = @('RogueRustSkins', 'RogueRustBlueprintManager', 'RogueRustCraftingController', 'RogueRustRandoSpawns')
    'World' = @('RogueRustDayNightScheduler')
}

function ConvertTo-HtmlText([string]$Text) { [System.Net.WebUtility]::HtmlEncode($Text) }

# ---- Read the repository -------------------------------------------------------------------

$readme = Get-Content (Join-Path $root 'README.md') -Raw
$plugins = foreach ($file in Get-ChildItem (Join-Path $root 'plugins') -Filter '*.cs' | Sort-Object Name) {
    $source = Get-Content $file.FullName -Raw
    if ($source -notmatch '\[Info\("([^"]+)",\s*"[^"]+",\s*"([^"]+)"\)\]') { throw "$($file.Name): no [Info] attribute found." }
    $version = $Matches[2]
    $stem = $file.BaseName

    # The catalogue row in README.md holds the short description; fall back to the [Description] attribute.
    $description = ''
    $row = [regex]::Match($readme, '\(docs/' + [regex]::Escape($stem) + '\.md\)\*\* \| `[^`]*` \| ([^|]*)\|')
    if ($row.Success) { $description = $row.Groups[1].Value.Trim() }
    elseif ($source -match '\[Description\("([^"]+)"\)\]') { $description = $Matches[1] }

    $category = 'More'
    foreach ($entry in $categories.GetEnumerator()) { if ($entry.Value -contains $stem) { $category = $entry.Key; break } }

    [pscustomobject]@{
        Stem        = $stem
        Title       = $stem -replace '^RogueRust', ''
        Version     = $version
        Description = $description -replace '^Preview\.\s*', ''
        Preview     = $description -match '^Preview\.'
        Category    = $category
        HasDoc      = Test-Path (Join-Path $root "docs/$stem.md")
        File        = $file
    }
}
if (-not $plugins) { throw 'No plugins found.' }

# ---- Page shell ----------------------------------------------------------------------------

function New-Page([string]$Title, [string]$Description, [string]$Body, [switch]$Landing) {
    $homeLink = if ($Landing) { '' } else { '<a class="nav-link" href="index.html">All plugins</a>' }
    @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>$(ConvertTo-HtmlText $Title)</title>
<meta name="description" content="$(ConvertTo-HtmlText $Description)">
<link rel="stylesheet" href="style.css">
</head>
<body>
<header class="site-header">
  <a class="brand" href="index.html"><span class="badge">RR</span><span class="brand-text"><strong>RogueRust</strong> Plugins</span></a>
  <nav>
    $homeLink
    <a class="nav-link" href="$extensionUrl">Get the DLL</a>
    <a class="nav-link" href="$repositoryUrl">GitHub</a>
  </nav>
</header>
<main>
$Body
</main>
<footer class="site-footer">
  <span>RogueRust plugins for Rust servers &middot; Oxide / Carbon</span>
  <span><a href="$repositoryUrl/issues">Report a problem</a></span>
</footer>
</body>
</html>
"@
}

# ---- Output folder -------------------------------------------------------------------------

if (Test-Path $OutputDirectory) { Remove-Item $OutputDirectory -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDirectory, (Join-Path $OutputDirectory 'plugins') | Out-Null
Copy-Item (Join-Path $root 'site/style.css') (Join-Path $OutputDirectory 'style.css')
foreach ($plugin in $plugins) { Copy-Item $plugin.File.FullName (Join-Path $OutputDirectory "plugins/$($plugin.File.Name)") }
Set-Content -Path (Join-Path $OutputDirectory 'CNAME') -Value $Domain -NoNewline
# Stops GitHub Pages running Jekyll over the generated files.
Set-Content -Path (Join-Path $OutputDirectory '.nojekyll') -Value '' -NoNewline

# ---- Home page -----------------------------------------------------------------------------

$sections = foreach ($category in @($categories.Keys) + 'More') {
    $members = @($plugins | Where-Object Category -eq $category)
    if ($members.Count -eq 0) { continue }
    $cards = foreach ($plugin in $members) {
        $preview = if ($plugin.Preview) { '<span class="tag">Preview</span>' } else { '' }
        $docLink = if ($plugin.HasDoc) { "<a class=`"button`" href=`"$($plugin.Stem).html`">Documentation</a>" } else { '' }
        @"
    <article class="card" data-search="$(ConvertTo-HtmlText ("$($plugin.Stem) $($plugin.Description)".ToLowerInvariant()))">
      <div class="card-head"><h3>$(ConvertTo-HtmlText $plugin.Title)</h3><span class="version">v$($plugin.Version)</span>$preview</div>
      <p>$(ConvertTo-HtmlText $plugin.Description)</p>
      <div class="card-actions">$docLink<a class="button ghost" href="plugins/$($plugin.File.Name)" download>Download .cs</a></div>
    </article>
"@
    }
    @"
  <section class="category">
    <h2>$(ConvertTo-HtmlText $category)</h2>
    <div class="grid">
$($cards -join "`n")
    </div>
  </section>
"@
}

$homeBody = @"
<section class="hero">
  <h1>Plugins for Rust servers</h1>
  <p>$($plugins.Count) maintained plugins built on the RogueRust extension. Every one is a single <code>.cs</code> file with its own documentation page.</p>
  <div class="hero-actions">
    <a class="button primary" href="$extensionUrl">1. Install the RogueRust DLL</a>
    <a class="button" href="#plugins">2. Pick your plugins</a>
  </div>
  <p class="note">Current plugins require RogueRust 4.4.63 or newer. Drop the DLL into <code>RustDedicated_Data/Managed</code>, then put each plugin in <code>oxide/plugins</code>.</p>
</section>
<section id="plugins">
  <input id="filter" class="filter" type="search" placeholder="Filter plugins, for example: heli, skins, admin" aria-label="Filter plugins">
  <p id="empty" class="note" hidden>No plugin matches that filter.</p>
$($sections -join "`n")
</section>
<script>
  const filter = document.getElementById('filter');
  filter.addEventListener('input', () => {
    const words = filter.value.toLowerCase().split(/\s+/).filter(Boolean);
    let shown = 0;
    document.querySelectorAll('.category').forEach(category => {
      let visible = 0;
      category.querySelectorAll('.card').forEach(card => {
        const match = words.every(word => card.dataset.search.includes(word));
        card.hidden = !match;
        if (match) visible++;
      });
      category.hidden = visible === 0;
      shown += visible;
    });
    document.getElementById('empty').hidden = shown !== 0;
  });
</script>
"@

New-Page -Landing -Title 'RogueRust Plugins for Rust servers' `
    -Description 'Maintained Rust server plugins built on the RogueRust extension, with documentation and downloads.' `
    -Body $homeBody | Set-Content -Path (Join-Path $OutputDirectory 'index.html') -Encoding utf8

# ---- Documentation pages -------------------------------------------------------------------

function Convert-DocLinks([string]$Html) {
    # Links are written for browsing the repository on GitHub; point them at the site's own pages.
    $Html = $Html -replace 'href="\.\./plugins/', 'href="plugins/'
    $Html = $Html -replace 'href="(\.\./)?README\.md"', 'href="index.html"'
    $Html = $Html -replace 'href="([A-Za-z0-9_\-]+)\.md(#[^"]*)?"', 'href="$1.html$2"'
    $Html
}

$pages = 0
foreach ($doc in Get-ChildItem (Join-Path $root 'docs') -Filter '*.md') {
    if ($doc.Name -eq 'README.md') { continue }   # the home page replaces the documentation index
    $html = Convert-DocLinks (ConvertFrom-Markdown -Path $doc.FullName).Html
    $plugin = $plugins | Where-Object Stem -eq $doc.BaseName
    $toolbar = ''
    $title = $doc.BaseName
    $summary = 'RogueRust plugin documentation.'
    if ($plugin) {
        $title = "$($plugin.Stem) $($plugin.Version)"
        $summary = $plugin.Description
        $toolbar = "<div class=`"doc-toolbar`"><a class=`"button primary`" href=`"plugins/$($plugin.File.Name)`" download>Download $($plugin.File.Name)</a><span class=`"version`">v$($plugin.Version)</span></div>"
    }
    New-Page -Title "$title - RogueRust Plugins" -Description $summary -Body "<article class=`"doc`">`n$toolbar`n$html`n</article>" |
        Set-Content -Path (Join-Path $OutputDirectory "$($doc.BaseName).html") -Encoding utf8
    $pages++
}

New-Page -Title 'Page not found - RogueRust Plugins' -Description 'Page not found.' `
    -Body '<section class="hero"><h1>Page not found</h1><p>That page does not exist. <a href="index.html">Back to the plugin list</a>.</p></section>' |
    Set-Content -Path (Join-Path $OutputDirectory '404.html') -Encoding utf8

Write-Host "Built $($plugins.Count) plugins and $pages documentation pages into $OutputDirectory"
