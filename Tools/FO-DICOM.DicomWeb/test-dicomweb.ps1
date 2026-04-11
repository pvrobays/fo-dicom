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

Write-Host "=============================================" -ForegroundColor Yellow
Write-Host " Done!" -ForegroundColor Yellow
Write-Host "=============================================" -ForegroundColor Yellow
