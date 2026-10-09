import { describe, it, expect } from 'vitest'
import fs from 'node:fs'
import path from 'node:path'

// AC c13c971d (a): không còn emoji làm icon trong mã UI (src/**/*.jsx, trừ test).
// Emoji = Unicode Extended_Pictographic (+ "⋮" dùng làm icon menu). Dùng <Icon name="…" /> thay thế.
const srcDir = path.resolve(__dirname, '..')

/**
 * Ngoại lệ: { file (tương đối src), reason }. Chỉ thêm khi ký tự là NỘI DUNG chứ không phải icon UI.
 * Hiện không có ngoại lệ.
 */
const EXCEPTIONS = []

const EMOJI = /[\p{Extended_Pictographic}⋮]/u

function jsxFiles(dir) {
  return fs.readdirSync(dir, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) return entry.name === 'test' ? [] : jsxFiles(full)
    return entry.name.endsWith('.jsx') && !/\.(test|spec)\./.test(entry.name) ? [full] : []
  })
}

describe('Không còn emoji làm icon trong src/**/*.jsx', () => {
  it('ngoại lệ phải có lý do', () => {
    for (const ex of EXCEPTIONS) {
      expect(ex.file).toBeTruthy()
      expect(ex.reason && ex.reason.length > 10).toBe(true)
    }
  })

  it('quét toàn bộ src/**/*.jsx → 0 emoji (trừ ngoại lệ có lý do)', () => {
    const excused = new Set(EXCEPTIONS.map((e) => e.file))
    const offenders = []
    for (const file of jsxFiles(srcDir)) {
      const rel = path.relative(srcDir, file).replace(/\\/g, '/')
      if (excused.has(rel)) continue
      fs.readFileSync(file, 'utf8').split('\n').forEach((line, i) => {
        if (EMOJI.test(line)) offenders.push(`${rel}:${i + 1}: ${line.trim().slice(0, 90)}`)
      })
    }
    expect(offenders).toEqual([])
  })
})
