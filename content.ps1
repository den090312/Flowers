# content.ps1 - Project files content analysis

# Create output file
$outputFile = "content-result-$(Get-Date -Format 'yyyyMMdd-HHmmss').txt"
Write-Host "Results will be saved to: $outputFile" -ForegroundColor Cyan

# Function to write to file and console
function Write-ToFileAndConsole {
    param([string]$Message, [string]$Color = "White")
    Write-Host $Message -ForegroundColor $Color
    Add-Content -Path $outputFile -Value $Message
}

# Start recording
"=== PROJECT ANALYSIS $(Get-Date) ===" | Add-Content -Path $outputFile
"" | Add-Content -Path $outputFile

# 1. Project structure analysis
Write-ToFileAndConsole "=== PROJECT STRUCTURE ANALYSIS ===" "Cyan"
"" | Add-Content -Path $outputFile

# All file extensions with counts
$allExtensions = Get-ChildItem -Recurse -File | Group-Object Extension | Sort-Object Count -Descending

Write-ToFileAndConsole "ALL FILE EXTENSIONS:" "Yellow"
$allExtensions | ForEach-Object {
    $line = "  $($_.Name.PadRight(10)) - $($_.Count) files"
    Write-ToFileAndConsole $line "White"
}
"" | Add-Content -Path $outputFile

# 2. File contents by categories
$fileTypes = @(
    @{Name = "YAML FILES (Kubernetes/Helm)"; Filter = "*.yaml"; Color = "Green"},
    @{Name = "YML FILES"; Filter = "*.yml"; Color = "Green"},
    @{Name = "DOCKERFILE"; Filter = "Dockerfile"; Color = "Blue"},
    @{Name = "C# FILES"; Filter = "*.cs"; Color = "Magenta"},
    @{Name = "CSPROJ FILES (PROJECTS)"; Filter = "*.csproj"; Color = "Cyan"},
    @{Name = "CONFIG FILES"; Filter = "*.config"; Color = "DarkCyan"},
    @{Name = "POWERSHELL SCRIPTS"; Filter = "*.ps1"; Color = "Red"},
    @{Name = "SLN FILES"; Filter = "*.sln"; Color = "DarkMagenta"},
    @{Name = "GITIGNORE"; Filter = ".gitignore"; Color = "DarkGray"}
)

foreach ($fileType in $fileTypes) {
    $files = Get-ChildItem -Recurse -Filter $fileType.Filter -File
    if ($files.Count -gt 0) {
        Write-ToFileAndConsole "=== $($fileType.Name) ===" $fileType.Color
        "" | Add-Content -Path $outputFile
        
        foreach ($file in $files) {
            Write-ToFileAndConsole "=== $($file.FullName) ===" $fileType.Color
            "--- File start: $($file.FullName) ---" | Add-Content -Path $outputFile
            try {
                $content = Get-Content $file.FullName -ErrorAction Stop
                $content | Add-Content -Path $outputFile
            }
            catch {
                "ERROR READING FILE: $($_.Exception.Message)" | Add-Content -Path $outputFile
            }
            "--- File end: $($file.FullName) ---" | Add-Content -Path $outputFile
            "" | Add-Content -Path $outputFile
        }
    }
}

# 3. Final info
Write-ToFileAndConsole "`n=== ANALYSIS COMPLETED ===" "Green"
Write-ToFileAndConsole "Full results saved to: $($PWD.Path)\$outputFile" "Cyan"
Write-ToFileAndConsole "File size: $((Get-Item $outputFile).Length) bytes" "Yellow"

# Open in notepad (optional)
$openInNotepad = Read-Host "Open result in Notepad? (y/n)"
if ($openInNotepad -eq 'y') {
    notepad $outputFile
}