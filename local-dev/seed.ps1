# Seed HAPI FHIR med norske syntetiske testdata
# Kjør: .\seed.ps1

$baseUrl = "http://localhost:8080/fhir"

Write-Host "Venter på HAPI FHIR..." -ForegroundColor Yellow
$attempts = 0
do {
    Start-Sleep -Seconds 3
    $attempts++
    try { $r = Invoke-RestMethod "$baseUrl/metadata" -TimeoutSec 3; break } catch {}
    Write-Host "  Forsok $attempts..."
} while ($attempts -lt 20)
Write-Host "HAPI FHIR er klar." -ForegroundColor Green

function Put-Resource($resourceType, $id, $body) {
    try {
        Invoke-RestMethod -Method Put -Uri "$baseUrl/$resourceType/$id" `
            -ContentType "application/fhir+json" `
            -Body ($body | ConvertTo-Json -Depth 10) | Out-Null
        Write-Host "  OK: $resourceType/$id" -ForegroundColor Green
    } catch {
        Write-Host "  FEIL: $resourceType/$id - $_" -ForegroundColor Red
    }
}

Write-Host "`nSeeder testdata..." -ForegroundColor Yellow

Put-Resource "Patient" "sophie-salt" @{
    resourceType = "Patient"
    id = "sophie-salt"
    identifier = @(@{ system = "urn:oid:2.16.578.1.12.4.1.4.1"; value = "01039012345" })
    name = @(@{ family = "Salt"; given = @("Sophie") })
    birthDate = "1990-03-01"
    gender = "female"
}

Put-Resource "Practitioner" "lege-ola" @{
    resourceType = "Practitioner"
    id = "lege-ola"
    identifier = @(
        @{ system = "urn:oid:2.16.578.1.12.4.1.4.4"; value = "1234567" },
        @{ system = "urn:oid:2.16.578.1.12.4.1.4.1"; value = "01017512345" }
    )
    name = @(@{ family = "Nordmann"; given = @("Ola") })
}

Put-Resource "Organization" "sandvika-legesenter" @{
    resourceType = "Organization"
    id = "sandvika-legesenter"
    identifier = @(
        @{ system = "urn:oid:2.16.578.1.12.4.1.4.101"; value = "987654321" },
        @{ system = "urn:oid:2.16.578.1.12.4.1.2"; value = "8765432" }
    )
    name = "Sandvika Legesenter"
}

Put-Resource "Encounter" "enc-sophie-001" @{
    resourceType = "Encounter"
    id = "enc-sophie-001"
    status = "finished"
    class = @{ system = "http://terminology.hl7.org/CodeSystem/v3-ActCode"; code = "AMB" }
    subject = @{ reference = "Patient/sophie-salt" }
    participant = @(@{ individual = @{ reference = "Practitioner/lege-ola" } })
    serviceProvider = @{ reference = "Organization/sandvika-legesenter" }
    period = @{ start = "2026-06-15T10:00:00+02:00"; end = "2026-06-15T10:30:00+02:00" }
}

Put-Resource "Condition" "cond-sophie-001" @{
    resourceType = "Condition"
    id = "cond-sophie-001"
    clinicalStatus = @{
        coding = @(@{ system = "http://terminology.hl7.org/CodeSystem/condition-clinical"; code = "active" })
    }
    code = @{
        coding = @(@{
            system = "http://hl7.org/fhir/sid/icd-10"
            code = "R55"
            display = "Synkope og kollaps"
        })
    }
    subject = @{ reference = "Patient/sophie-salt" }
    encounter = @{ reference = "Encounter/enc-sophie-001" }
    recordedDate = "2026-06-15"
}

Write-Host "`nFerdig! Verifiser pa:" -ForegroundColor Green
Write-Host "  $baseUrl/Patient/sophie-salt"
Write-Host "  $baseUrl/Practitioner/lege-ola"
Write-Host "  $baseUrl/Organization/sandvika-legesenter"
Write-Host "  $baseUrl/Encounter/enc-sophie-001"
Write-Host "  $baseUrl/Condition/cond-sophie-001"
