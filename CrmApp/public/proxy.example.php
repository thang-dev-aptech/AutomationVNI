<?php
/**
 * Reverse Proxy bằng PHP cho DirectAdmin CrmApp (subdomain crm.vni.edu.vn)
 * Chuyển tiếp toàn bộ request /api/... sang backend .NET chạy cùng server (127.0.0.1:5000)
 */
error_reporting(0);
ini_set('display_errors', 0);

$backendUrl = "http://127.0.0.1:5000";
$requestUri = $_SERVER['REQUEST_URI'];
$targetUrl = $backendUrl . $requestUri;

$ch = curl_init();
curl_setopt($ch, CURLOPT_URL, $targetUrl);
curl_setopt($ch, CURLOPT_CUSTOMREQUEST, $_SERVER['REQUEST_METHOD']);

// Xử lý Body và Upload file multipart
$contentType = isset($_SERVER['CONTENT_TYPE']) ? $_SERVER['CONTENT_TYPE'] : '';
if (stripos($contentType, 'multipart/form-data') !== false) {
    $postData = $_POST;
    foreach ($_FILES as $key => $file) {
        if (is_array($file['tmp_name'])) {
            foreach ($file['tmp_name'] as $i => $tmpName) {
                $postData[$key . "[$i]"] = new CURLFile($tmpName, $file['type'][$i], $file['name'][$i]);
            }
        } else {
            $postData[$key] = new CURLFile($file['tmp_name'], $file['type'], $file['name']);
        }
    }
    curl_setopt($ch, CURLOPT_POSTFIELDS, $postData);
} else {
    $input = file_get_contents('php://input');
    if ($input) {
        curl_setopt($ch, CURLOPT_POSTFIELDS, $input);
    }
}

// Chuyển tiếp Headers (bỏ qua Host & Content-Length để tránh lỗi)
$headers = array();
if (function_exists('getallheaders')) {
    foreach (getallheaders() as $key => $value) {
        $lowerKey = strtolower($key);
        if ($lowerKey !== 'host' && $lowerKey !== 'content-length') {
            $headers[] = "$key: $value";
        }
    }
}
curl_setopt($ch, CURLOPT_HTTPHEADER, $headers);
curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
curl_setopt($ch, CURLOPT_HEADER, true);
curl_setopt($ch, CURLOPT_FOLLOWLOCATION, false);

$response = curl_exec($ch);
$headerSize = curl_getinfo($ch, CURLOPT_HEADER_SIZE);
$responseHeaders = substr($response, 0, $headerSize);
$responseBody = substr($response, $headerSize);
$httpCode = curl_getinfo($ch, CURLINFO_HTTP_CODE);
curl_close($ch);

http_response_code($httpCode);
$headerArray = explode("\r\n", $responseHeaders);
foreach ($headerArray as $header) {
    if (trim($header) != '' && stripos($header, 'Transfer-Encoding') === false) {
        header($header, false);
    }
}
echo $responseBody;
