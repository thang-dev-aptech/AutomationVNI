import { describe, it, expect } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

const crmSrcDir = path.resolve(__dirname, '../')

function getAllSourceFiles(dir) {
  let results = []
  const list = fs.readdirSync(dir, { withFileTypes: true })
  for (const dirent of list) {
    const fullPath = path.join(dir, dirent.name)
    if (dirent.isDirectory()) {
      results = results.concat(getAllSourceFiles(fullPath))
    } else if (/\.(jsx?|tsx?|css|json)$/.test(dirent.name)) {
      results.push(fullPath)
    }
  }
  return results
}

describe('AC crm-app-separation-check (75116571)', () => {
  it('proves CrmApp/src does not import any path from ClientApp/src or ClientApp', () => {
    const files = getAllSourceFiles(crmSrcDir)
    expect(files.length).toBeGreaterThan(0)

    const restrictedPattern = /(['"`])(?:(?:\.\.\/)+ClientApp\/src|(?:\.\.\/)+ClientApp|.*\/ClientApp\/src|ClientApp\/src)\b/i
    const violations = []

    for (const file of files) {
      const content = fs.readFileSync(file, 'utf8')
      const lines = content.split('\n')
      lines.forEach((line, index) => {
        if (
          restrictedPattern.test(line) ||
          /from\s+['"][^'"]*ClientApp/i.test(line) ||
          /import\s*\([^)]*ClientApp/i.test(line)
        ) {
          violations.push({
            file: path.relative(crmSrcDir, file),
            line: index + 1,
            content: line.trim(),
          })
        }
      })
    }

    expect(violations).toEqual([])
  })
})
