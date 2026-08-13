<?php
/**
 * Proxy tối giản cho trang tin tĩnh (public_html/news/) gọi sang backend mà không dính CORS —
 * browser gọi file PHP này CÙNG GỐC với trang tin, PHP mới là bên gọi chéo domain
 * (server-to-server, không bị trình duyệt chặn) — cùng ý tưởng với proxy.php đang dùng cho
 * frontend React.
 *
 * $BACKEND_BASE trỏ 127.0.0.1:5000 (không phải https://auto.vni.edu.vn) vì public_html/news
 * và backend cùng nằm trên MỘT server/account DirectAdmin (../../vni.edu.vn/public_html/news
 * là thư mục anh em của publish_output) — gọi localhost nhanh hơn, không tốn SSL handshake ra
 * ngoài, giống hệt cách proxy.php hiện tại đang gọi backend.
 *
 * Cách dùng: đặt file này VÀO ĐÚNG thư mục public_html/news/ (cùng cấp với index.html), rồi
 * site.js sẽ gọi "/news/api-proxy.php?...".
 *
 * Khác với proxy.php (forward mọi path/method — hợp lý cho frontend React đã có JWT sau lưng):
 * file này CHỈ forward đúng 2 đường dẫn backend được whitelist cứng dưới đây, vì đây là proxy
 * PHÍA TRANG TIN CÔNG KHAI, không có lớp đăng nhập nào che — để không biến thành open proxy
 * bị lợi dụng gọi bừa sang các API admin khác của backend.
 */

$BACKEND_BASE = 'http://127.0.0.1:5000';

$ALLOWED = [
    'search'    => ['method' => 'GET',  'path' => '/api/news-public/search'],
    'subscribe' => ['method' => 'POST', 'path' => '/api/news-public/subscribe'],
];

$key = isset($_GET['path']) ? $_GET['path'] : '';
if (!isset($ALLOWED[$key])) {
    http_response_code(400);
    header('Content-Type: application/json');
    echo json_encode(['success' => false, 'message' => 'Không hỗ trợ']);
    exit;
}

$route = $ALLOWED[$key];
if ($_SERVER['REQUEST_METHOD'] !== $route['method']) {
    http_response_code(405);
    exit;
}

$url = $BACKEND_BASE . $route['path'];
if ($route['method'] === 'GET') {
    $qs = $_GET;
    unset($qs['path']);
    if (!empty($qs)) $url .= '?' . http_build_query($qs);
}

$ch = curl_init($url);
curl_setopt($ch, CURLOPT_RETURNTRANSFER, true);
curl_setopt($ch, CURLOPT_TIMEOUT, 15);

if ($route['method'] === 'POST') {
    $body = file_get_contents('php://input');
    curl_setopt($ch, CURLOPT_POST, true);
    curl_setopt($ch, CURLOPT_POSTFIELDS, $body);
    curl_setopt($ch, CURLOPT_HTTPHEADER, ['Content-Type: application/json']);
}

$response = curl_exec($ch);
$status = curl_getinfo($ch, CURLINFO_HTTP_CODE);
curl_close($ch);

http_response_code($status ?: 502);
header('Content-Type: application/json');
echo $response !== false ? $response : json_encode(['success' => false, 'message' => 'Không gọi được backend']);
