import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

const crmSrcDir = path.resolve(__dirname, '../src')

function getFiles(dir) {
  let results = []
  const list = fs.readdirSync(dir, { withFileTypes: true })
  for (const dirent of list) {
    const fullPath = path.join(dir, dirent.name)
    if (dirent.isDirectory()) {
      results = results.concat(getFiles(fullPath))
    } else if (/\.(jsx?|tsx?|css|json)$/.test(dirent.name)) {
      results.push(fullPath)
    }
  }
  return results
}

const files = getFiles(crmSrcDir)
const violations = []

const restrictedPattern = /(['"`])(?:(?:\.\.\/)+ClientApp\/src|(?:\.\.\/)+ClientApp|.*\/ClientApp\/src|ClientApp\/src)\b/i

for (const file of files) {
  const content = fs.readFileSync(file, 'utf8')
  const lines = content.split('\n')
  lines.forEach((line, index) => {
    if (restrictedPattern.test(line) || /from\s+['"][^'"]*ClientApp/i.test(line) || /import\s*\([^)]*ClientApp/i.test(line)) {
      violations.push({
        file: path.relative(crmSrcDir, file),
        line: index + 1,
        content: line.trim(),
      })
    }
  })
}

if (violations.length > 0) {
  console.error('\n❌ VIOLATION: CrmApp contains restricted imports from ClientApp:')
  for (const v of violations) {
    console.error(`   ${v.file}:${v.line} -> ${v.content}`)
  }
  process.exit(1)
} else {
  console.log(`✅ Separation check passed: ${files.length} source files checked, 0 imports from ClientApp/src found.`)
}
