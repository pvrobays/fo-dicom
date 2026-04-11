<#
.SYNOPSIS
    DICOMweb QIDO-RS Test Script

.DESCRIPTION
    Tests the demo DICOMweb application (FO-DICOM.DicomWeb) using curl.
    Works on Windows (PowerShell) and Linux/macOS (pwsh).

    Prerequisites:
      1. Start the demo app:
         dotnet run --project Tools/FO-DICOM.DicomWeb/FO-DICOM.DicomWeb.csproj

      2. Run this script:
         pwsh Tools/FO-DICOM.DicomWeb/test-dicomweb.ps1

    The app listens on https://localhost:7215 by default (see launchSettings.json).

.PARAMETER BaseUrl
    The base URL of the DICOMweb service. Defaults to https://localhost:7215/dicomweb
#>
param(
    [string]$BaseUrl = "https://localhost:7215/dicomweb"
)

$ErrorActionPreference = "Continue"

function Run-Test {
    param(
        [string]$Label,
        [string]$Url,
        [string]$Description = "",
        [int]$ExpectedStatus = 200,
        [int]$MaxBodyChars = 500
    )

    Write-Host ">>> $Label" -ForegroundColor Cyan
    if ($Description) { Write-Host "    $Description" -ForegroundColor DarkGray }
    Write-Host "    $Url" -ForegroundColor DarkGray

    try {
        $response = Invoke-WebRequest -Uri $Url -SkipCertificateCheck -ErrorAction Stop
        $status = $response.StatusCode
        $body = $response.Content
    }
    catch {
        if ($_.Exception.Response) {
            $status = [int]$_.Exception.Response.StatusCode
            $body = $_.ErrorDetails.Message
            if (-not $body) { $body = $_.Exception.Message }
        }
        else {
            Write-Host "    ERROR: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host ""
            return
        }
    }

    # Truncate body for display
    if ($body.Length -gt $MaxBodyChars) {
        Write-Host ($body.Substring(0, $MaxBodyChars) + "...") -ForegroundColor White
    }
    elseif ($body) {
        Write-Host $body -ForegroundColor White
    }

    # Status check
    if ($status -eq $ExpectedStatus) {
        Write-Host "--- HTTP $status (expected $ExpectedStatus) ---" -ForegroundColor Green
    }
    else {
        Write-Host "--- HTTP $status (EXPECTED $ExpectedStatus) ---" -ForegroundColor Red
    }
    Write-Host ""
}

Write-Host "=============================================" -ForegroundColor Yellow
Write-Host " DICOMweb QIDO-RS Test Script" -ForegroundColor Yellow
Write-Host " Base URL: $BaseUrl" -ForegroundColor Yellow
Write-Host "=============================================" -ForegroundColor Yellow
Write-Host ""

# ===========================================================================
#  Basic QIDO
# ===========================================================================

Run-Test -Label "1. All studies (no filters)" `
    -Url "$BaseUrl/studies"

Run-Test -Label "2. Filter by PatientID" `
    -Url "$BaseUrl/studies?PatientID=11235813"

Run-Test -Label "3. Multiple filters (PatientID + StudyDate)" `
    -Url "$BaseUrl/studies?PatientID=11235813&StudyDate=20130509"

Run-Test -Label "4. Include fields (CSV hex tags)" `
    -Url "$BaseUrl/studies?PatientID=11235813&includefield=00081048,00081049,00081060"

Run-Test -Label "5. Pagination (limit=2, offset=1)" `
    -Url "$BaseUrl/studies?limit=2&offset=1"

Run-Test -Label "6. Fuzzy matching" `
    -Url "$BaseUrl/studies?PatientName=SMITH&fuzzymatching=true"

# ===========================================================================
#  SEQUENCE SUPPORT (new features)
# ===========================================================================

Write-Host "=============================================" -ForegroundColor Yellow
Write-Host " Sequence Support Tests" -ForegroundColor Yellow
Write-Host "=============================================" -ForegroundColor Yellow
Write-Host ""

Run-Test -Label "7. includefield dot notation (hex tags)" `
    -Url "$BaseUrl/studies?includefield=00081115.00080060" `
    -Description "ReferencedSeriesSequence.Modality" `
    -MaxBodyChars 800

Run-Test -Label "8. includefield dot notation (keywords)" `
    -Url "$BaseUrl/studies?includefield=OtherPatientIDsSequence.PatientID" `
    -MaxBodyChars 800

Run-Test -Label "9. includefield bare SQ tag" `
    -Url "$BaseUrl/studies?includefield=RequestAttributesSequence" `
    -Description "Returns entire sequence in response" `
    -MaxBodyChars 800

Run-Test -Label "10. Query filter dot notation (hex tags)" `
    -Url "$BaseUrl/studies?00101002.00100020=11235813" `
    -Description "OtherPatientIDsSequence.PatientID=11235813" `
    -MaxBodyChars 800

Run-Test -Label "11. Query filter dot notation (keywords)" `
    -Url "$BaseUrl/studies?OtherPatientIDsSequence.PatientID=11235813" `
    -MaxBodyChars 800

Run-Test -Label "12. DICOM spec example (PS3.18 Section 10.6.1.2)" `
    -Url "$BaseUrl/studies?00100010=SMITH*&00101002.00100020=11235813&limit=25" `
    -Description "PatientName wildcard + sequence filter + limit" `
    -MaxBodyChars 800

Run-Test -Label "13. Combined filter + sequence includefields" `
    -Url "$BaseUrl/studies?PatientID=11235813&includefield=OtherPatientIDsSequence.PatientID&includefield=RequestAttributesSequence" `
    -MaxBodyChars 800

Run-Test -Label "14. Bare SQ tag as query param (treated as include)" `
    -Url "$BaseUrl/studies?RequestAttributesSequence=ignored" `
    -Description "Value is ignored, sequence is returned as include field" `
    -MaxBodyChars 800

# ===========================================================================
#  ERROR CASES
# ===========================================================================

Write-Host "=============================================" -ForegroundColor Yellow
Write-Host " Error Case Tests (expect 400 Bad Request)" -ForegroundColor Yellow
Write-Host "=============================================" -ForegroundColor Yellow
Write-Host ""

Run-Test -Label "15. Unknown query parameter" `
    -Url "$BaseUrl/studies?NotADicomTag=value" `
    -ExpectedStatus 400

Run-Test -Label "16. Non-SQ intermediate in dot notation" `
    -Url "$BaseUrl/studies?PatientID.PatientName=value" `
    -ExpectedStatus 400

Run-Test -Label "17. Invalid segment in dot notation includefield" `
    -Url "$BaseUrl/studies?includefield=NotATag.PatientID" `
    -ExpectedStatus 400

Run-Test -Label "18. Negative offset" `
    -Url "$BaseUrl/studies?offset=-1" `
    -ExpectedStatus 400

# ===========================================================================
#  DATE RANGE MATCHING (PS3.4 C.2.2.2.5)
# ===========================================================================

Write-Host "=============================================" -ForegroundColor Yellow
Write-Host " Date Range Matching Tests" -ForegroundColor Yellow
Write-Host "=============================================" -ForegroundColor Yellow
Write-Host ""

Run-Test -Label "19. StudyDate bounded range" `
    -Url "$BaseUrl/studies?StudyDate=20230101-20231231" `
    -Description "All studies with StudyDate between 2023-01-01 and 2023-12-31 (inclusive)" `
    -MaxBodyChars 800

Run-Test -Label "20. StudyDate open-start range" `
    -Url "$BaseUrl/studies?StudyDate=-20200101" `
    -Description "All studies with StudyDate on or before 2020-01-01" `
    -MaxBodyChars 800

Run-Test -Label "21. StudyDate open-end range" `
    -Url "$BaseUrl/studies?StudyDate=20220101-" `
    -Description "All studies with StudyDate on or after 2022-01-01" `
    -MaxBodyChars 800

Run-Test -Label "22. StudyDate range + PatientName filter" `
    -Url "$BaseUrl/studies?StudyDate=20220101-20241231&PatientName=SMITH*" `
    -Description "Date range combined with a patient name wildcard filter" `
    -MaxBodyChars 800

Run-Test -Label "23. StudyTime bounded range" `
    -Url "$BaseUrl/studies?StudyTime=090000-170000" `
    -Description "All studies with StudyTime between 09:00 and 17:00" `
    -MaxBodyChars 800

# ===========================================================================
#  UID LIST MATCHING (PS3.18 Section 8.3.4.1 / PS3.4 C.2.2.2.2)
# ===========================================================================

Write-Host "=============================================" -ForegroundColor Yellow
Write-Host " UID List Matching Tests" -ForegroundColor Yellow
Write-Host "=============================================" -ForegroundColor Yellow
Write-Host ""

Run-Test -Label "24. Single StudyInstanceUID filter" `
    -Url "$BaseUrl/studies?StudyInstanceUID=1.2.840.10008.5.1.4.1.1.2" `
    -Description "Matches studies with a specific StudyInstanceUID" `
    -MaxBodyChars 800

Run-Test -Label "25. Multi-UID list (comma-separated)" `
    -Url "$BaseUrl/studies?StudyInstanceUID=1.2.840.10008.5.1.4.1.1.2,1.2.840.10008.5.1.4.1.1.4,1.2.840.10008.5.1.4.1.1.128" `
    -Description "Matches studies whose UID is any of the three specified UIDs (PS3.4 C.2.2.2.2)" `
    -MaxBodyChars 800

# ===========================================================================
#  SERIES ENDPOINTS (PS3.18 Table 10.6.1-1)
# ===========================================================================

Write-Host "=============================================" -ForegroundColor Yellow
Write-Host " Series Endpoint Tests" -ForegroundColor Yellow
Write-Host "=============================================" -ForegroundColor Yellow
Write-Host ""

# Grab the first StudyInstanceUID from the all-studies response so we can
# use it in scoped series / instance queries below.
$studyUid = $null
$seriesUid = $null
try {
    $allStudies = Invoke-WebRequest -Uri "$BaseUrl/studies" -SkipCertificateCheck -ErrorAction Stop
    $studiesJson = $allStudies.Content | ConvertFrom-Json
    # JSON keys are hex tags: 0020000D = StudyInstanceUID
    $studyUid = $studiesJson[0]."0020000D".Value[0]
}
catch { }

Run-Test -Label "26. All series (no scope, no filters)" `
    -Url "$BaseUrl/series" `
    -Description "PS3.18 Table 10.6.1-1: All series resource" `
    -MaxBodyChars 800

Run-Test -Label "27. All series filtered by Modality" `
    -Url "$BaseUrl/series?Modality=CT" `
    -Description "Series-level query with Modality=CT filter" `
    -MaxBodyChars 800

Run-Test -Label "28. Study's series (scoped)" `
    -Url $(if ($studyUid) { "$BaseUrl/studies/$studyUid/series" } else { "$BaseUrl/studies/1.2.3/series" }) `
    -Description "Series for a specific study (studyInstanceUID from route)" `
    -MaxBodyChars 800

Run-Test -Label "29. Study's series filtered by Modality" `
    -Url $(if ($studyUid) { "$BaseUrl/studies/$studyUid/series?Modality=CT" } else { "$BaseUrl/studies/1.2.3/series?Modality=CT" }) `
    -Description "Scoped series + Modality query param" `
    -MaxBodyChars 800

# ===========================================================================
#  INSTANCE ENDPOINTS (PS3.18 Table 10.6.1-1)
# ===========================================================================

Write-Host "=============================================" -ForegroundColor Yellow
Write-Host " Instance Endpoint Tests" -ForegroundColor Yellow
Write-Host "=============================================" -ForegroundColor Yellow
Write-Host ""

# Grab the first SeriesInstanceUID from the all-series response.
try {
    $allSeries = Invoke-WebRequest -Uri "$BaseUrl/series" -SkipCertificateCheck -ErrorAction Stop
    $seriesJson = $allSeries.Content | ConvertFrom-Json
    # 0020000E = SeriesInstanceUID
    $seriesUid = $seriesJson[0]."0020000E".Value[0]
}
catch { }

Run-Test -Label "30. All instances (no scope, no filters)" `
    -Url "$BaseUrl/instances" `
    -Description "PS3.18 Table 10.6.1-1: All instances resource" `
    -MaxBodyChars 800

Run-Test -Label "31. All instances filtered by SOPClassUID" `
    -Url "$BaseUrl/instances?SOPClassUID=1.2.840.10008.5.1.4.1.1.2" `
    -Description "Instance-level query filtered by SOPClassUID (CT Image Storage)" `
    -MaxBodyChars 800

Run-Test -Label "32. Study's instances (scoped by study)" `
    -Url $(if ($studyUid) { "$BaseUrl/studies/$studyUid/instances" } else { "$BaseUrl/studies/1.2.3/instances" }) `
    -Description "Instances for a specific study (studyInstanceUID from route)" `
    -MaxBodyChars 800

Run-Test -Label "33. Study's series' instances (scoped by study + series)" `
    -Url $(if ($studyUid -and $seriesUid) { "$BaseUrl/studies/$studyUid/series/$seriesUid/instances" } else { "$BaseUrl/studies/1.2.3/series/4.5.6/instances" }) `
    -Description "Instances for a specific study+series (both UIDs from route)" `
    -MaxBodyChars 800

Run-Test -Label "34. Study's series' instances filtered by SOPClassUID" `
    -Url $(if ($studyUid -and $seriesUid) { "$BaseUrl/studies/$studyUid/series/$seriesUid/instances?SOPClassUID=1.2.840.10008.5.1.4.1.1.2" } else { "$BaseUrl/studies/1.2.3/series/4.5.6/instances?SOPClassUID=1.2.840.10008.5.1.4.1.1.2" }) `
    -Description "Scoped instance query with SOPClassUID filter" `
    -MaxBodyChars 800

Run-Test -Label "35. Instance pagination (limit=2, offset=0)" `
    -Url "$BaseUrl/instances?limit=2&offset=0" `
    -Description "Pagination on the all-instances endpoint" `
    -MaxBodyChars 800

Write-Host "=============================================" -ForegroundColor Yellow
Write-Host " Done!" -ForegroundColor Yellow
Write-Host "=============================================" -ForegroundColor Yellow
