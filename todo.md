# DICOMweb TODO

## Deferred / low priority

1. **Authentication hooks** (demo placeholder)
   - `Tools/FO-DICOM.DicomWeb/MyDicomWebServer.cs:18`

2. **Demo "get from DB" placeholder comments**
   - `Tools/FO-DICOM.DicomWeb/MyDicomWebServer.cs:38-42`

## WADO-RS: future work

3. **Transfer syntax negotiation / transcoding** (PS3.18 Section 8.7.3.5.2)
   - Currently WADO-RS serves instances in their stored transfer syntax and ignores
     the `transfer-syntax` parameter in the `Accept` header.
   - A future implementation should parse the Accept header's `transfer-syntax`
     parameter, and either reject unsupported syntaxes with 406, or transcode on
     the fly (requires a pixel data codec pipeline).
   - Relevant code: `WadoResponseWriter.WriteMultipartDicomResponseAsync`

4. **Additional WADO-RS resource types** (PS3.18 Section 10.4.1.1)
   - Rendered Resources (`/rendered`) — JPEG/PNG image rendering
   - Thumbnail Resources (`/thumbnail`) — single rendered thumbnail
   - Bulk Data Resources (`/bulkdata`) — raw octet-stream data element extraction
   - Pixel Data Resources (`/pixeldata`, `/frames/{frames}`) — frame-level pixel data
   - Rendered MPR Volume (`/renderedmpr`) — multiplanar reformatting
   - Rendered 3D Volume (`/rendered3d`) — 3D volume rendering

## STOW-RS: not yet started

5. **STOW-RS (Store Transaction)** per PS3.18 Section 10.5
   - `POST /studies` — store DICOM instances
   - `POST /studies/{study}` — store into a specific study
   - Response: `200 OK` with a DICOM dataset describing stored/failed SOPs
