#!/usr/bin/env pwsh

Write-Host "Checking Elasticsearch indices..." -ForegroundColor Cyan
Write-Host ""

# Проверка доступности Elasticsearch
try {
    $health = Invoke-RestMethod "http://localhost:9200/_cluster/health" -ErrorAction Stop
} catch {
    Write-Host "Error: Cannot connect to Elasticsearch at http://localhost:9200" -ForegroundColor Red
    exit 1
}

# Получить список индексов с метаданными
Write-Host "=== Indices Overview ===" -ForegroundColor Yellow
Write-Host ""

$indices = Invoke-RestMethod 'http://localhost:9200/_cat/indices/dotnet-logs-*?format=json'

if ($indices.Count -eq 0) {
    Write-Host "No indices found matching 'dotnet-logs-*'" -ForegroundColor Yellow
    Write-Host "This is normal if you haven't sent any logs yet." -ForegroundColor Cyan
    exit 0
}

# Сортировка по дате создания
$indices = $indices | Sort-Object { [long]$_.'creation.date' }

# Форматированный вывод
$table = $indices | ForEach-Object {
    $creationDate = [DateTimeOffset]::FromUnixTimeMilliseconds([long]$_.'creation.date').DateTime
    $age = (Get-Date) - $creationDate
    
    [PSCustomObject]@{
        Index = $_.index
        Created = $creationDate.ToString("yyyy-MM-dd HH:mm:ss")
        Age = if ($age.Days -gt 0) { "$($age.Days)d $($age.Hours)h" } else { "$($age.Hours)h $($age.Minutes)m" }
        Docs = [int]$_.'docs.count'
        Size = $_.'store.size'
        Status = $_.status
        Health = $_.health
    }
}

$table | Format-Table -AutoSize

# Получить статус ILM
Write-Host ""
Write-Host "=== ILM Status ===" -ForegroundColor Yellow
Write-Host ""

try {
    $ilmExplain = Invoke-RestMethod 'http://localhost:9200/dotnet-logs-*/_ilm/explain'
    
    $ilmTable = $ilmExplain.indices.PSObject.Properties | ForEach-Object {
        $index = $_.Value
        [PSCustomObject]@{
            Index = $index.index
            Phase = $index.phase
            Action = $index.action
            Age = $index.age
            "Will Delete In" = if ($index.phase -eq "hot") { 
                "7 days from creation" 
            } elseif ($index.phase -eq "delete") {
                "Soon (in delete phase)"
            } else {
                "N/A"
            }
        }
    } | Sort-Object Index
    
    $ilmTable | Format-Table -AutoSize
} catch {
    Write-Host "Could not retrieve ILM status" -ForegroundColor Yellow
}

# Итоговая статистика
Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Yellow
$totalDocs = ($indices | Measure-Object -Property 'docs.count' -Sum).Sum
$totalIndices = $indices.Count
Write-Host "Total Indices: $totalIndices"
Write-Host "Total Documents: $totalDocs"
Write-Host "Oldest Index: $($table[0].Index) (created $($table[0].Created))"
Write-Host "Newest Index: $($table[-1].Index) (created $($table[-1].Created))"
Write-Host ""
