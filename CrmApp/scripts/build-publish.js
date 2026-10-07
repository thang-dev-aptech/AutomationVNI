import fs from 'node:fs'
import path from 'node:path'
import { execSync } from 'node:child_process'
import { fileURLToPath } from 'node:url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

const crmAppDir = path.resolve(__dirname, '..')
const repoRootDir = path.resolve(crmAppDir, '..')
const distDir = path.resolve(crmAppDir, 'dist')
const publicHtaccess = path.resolve(crmAppDir, 'public/.htaccess')
const distHtaccess = path.resolve(distDir, '.htaccess')

console.log('--- Bắt đầu đóng gói publish_crm.zip ---')

if (!fs.existsSync(distDir)) {
  console.error('❌ Thư mục dist không tồn tại! Chạy vite build trước.')
  process.exit(1)
}

// 1. Bảo đảm .htaccess mẫu có trong dist
if (!fs.existsSync(distHtaccess) && fs.existsSync(publicHtaccess)) {
  fs.copyFileSync(publicHtaccess, distHtaccess)
  console.log('✓ Đã sao chép .htaccess vào dist')
}

// 2. Kiểm tra không có appsettings hoặc .env lọt vào dist
const forbiddenFiles = []
function scanForbidden(dir) {
  const items = fs.readdirSync(dir, { withFileTypes: true })
  for (const item of items) {
    const full = path.join(dir, item.name)
    if (item.isDirectory()) {
      scanForbidden(full)
    } else {
      const lower = item.name.toLowerCase()
      if (lower.startsWith('appsettings') || lower.startsWith('.env')) {
        forbiddenFiles.push(full)
      }
    }
  }
}
scanForbidden(distDir)

if (forbiddenFiles.length > 0) {
  console.error('❌ PHÁT HIỆN FILE NHẠY CẢM TRONG DIST:')
  for (const f of forbiddenFiles) {
    console.error('  ', f)
  }
  process.exit(1)
}
console.log('✓ Kiểm tra an toàn: Không chứa appsettings hay .env')

// 3. Kiểm tra index.html, assets, .htaccess
const hasIndex = fs.existsSync(path.join(distDir, 'index.html'))
const hasAssets = fs.existsSync(path.join(distDir, 'assets'))
const hasHtaccess = fs.existsSync(distHtaccess)

if (!hasIndex || !hasAssets || !hasHtaccess) {
  console.error(`❌ Thiếu file bắt buộc trong dist: index=${hasIndex}, assets=${hasAssets}, .htaccess=${hasHtaccess}`)
  process.exit(1)
}
console.log('✓ Đã xác minh dist có đầy đủ index.html, assets/ và .htaccess')

// 4. Tạo gói zip
const crmZipPath = path.resolve(crmAppDir, 'publish_crm.zip')
const rootZipPath = path.resolve(repoRootDir, 'publish_crm.zip')

if (fs.existsSync(crmZipPath)) fs.unlinkSync(crmZipPath)
if (fs.existsSync(rootZipPath)) fs.unlinkSync(rootZipPath)

try {
  // Zip nội dung bên trong dist (bao gồm cả hidden file như .htaccess)
  execSync(`cd "${distDir}" && zip -r "${crmZipPath}" . -x "*.DS_Store"`, { stdio: 'inherit' })
  // Đồng bộ sang root repo
  fs.copyFileSync(crmZipPath, rootZipPath)
  console.log(`✓ Đã tạo thành công: ${crmZipPath}`)
  console.log(`✓ Đã sao chép sang: ${rootZipPath}`)
} catch (err) {
  console.error('❌ Lỗi khi nén zip:', err)
  process.exit(1)
}

// 5. Kiểm tra nội dung trong zip
try {
  const zipListing = execSync(`unzip -l "${crmZipPath}"`, { encoding: 'utf8' })
  const containsIndex = zipListing.includes('index.html')
  const containsHtaccess = zipListing.includes('.htaccess')
  const containsAssets = zipListing.includes('assets/')
  const containsSecrets = /appsettings|\.env/i.test(zipListing)

  if (!containsIndex || !containsHtaccess || !containsAssets || containsSecrets) {
    console.error('❌ Kiểm tra gói zip không đạt yêu cầu!')
    console.error(`index: ${containsIndex}, .htaccess: ${containsHtaccess}, assets: ${containsAssets}, secrets: ${containsSecrets}`)
    process.exit(1)
  }
  console.log('✅ Đóng gói publish_crm.zip hoàn tất và đạt chuẩn!')
} catch (err) {
  console.error('❌ Lỗi khi kiểm tra zip:', err)
  process.exit(1)
}
