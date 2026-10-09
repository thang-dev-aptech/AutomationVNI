import React from 'react'
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render } from '@testing-library/react'
import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { Icon, ICON_MAP } from '../shared/components/Icon'
import { PlatformLogo } from '../features/inbox/components/PlatformLogo'
import { SourceBadge } from '../features/inbox/components/SourceBadge'

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)
const srcDir = path.resolve(__dirname, '..')

function getAllSourceFiles(dir) {
  let results = []
  const entries = fs.readdirSync(dir, { withFileTypes: true })
  for (const entry of entries) {
    const fullPath = path.join(dir, entry.name)
    if (entry.isDirectory()) {
      results = results.concat(getAllSourceFiles(fullPath))
    } else if (/\.(jsx?|tsx?)$/.test(entry.name)) {
      results.push(fullPath)
    }
  }
  return results
}

describe('Requirement d39fd882, AC c13c971d — Nền tảng Icon lucide-react trong CrmApp', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  describe('AC c13c971d (b) — Icon mapping, rendering SVG của lucide và thuộc tính a11y', () => {
    it('mọi tên trong ICON_MAP render <svg> của lucide với kích thước và màu chuẩn', () => {
      const names = Object.keys(ICON_MAP)
      expect(names.length).toBeGreaterThanOrEqual(40)

      names.forEach((name) => {
        const { container, unmount } = render(<Icon name={name} data-testid={`icon-${name}`} />)
        const svg = container.querySelector('svg')
        expect(svg, `Icon "${name}" phải render thẻ <svg>`).not.toBeNull()
        expect(svg.classList.contains('lucide'), `Icon "${name}" phải có class "lucide"`).toBe(true)
        expect(svg).toHaveAttribute('width', '18')
        expect(svg).toHaveAttribute('height', '18')
        expect(svg).toHaveAttribute('stroke', 'currentColor')
        expect(svg).toHaveAttribute('stroke-width', '2')
        expect(svg).toHaveAttribute('aria-hidden', 'true')
        unmount()
      })
    })

    it('giữ đủ 12 tên gốc đã dùng trước đây trong CrmApp', () => {
      const originalNames = [
        'inbox',
        'customers',
        'tasks',
        'settings',
        'menu',
        'close',
        'logout',
        'search',
        'plus',
        'send',
        'user',
        'tag',
      ]
      originalNames.forEach((name) => {
        expect(ICON_MAP[name], `ICON_MAP phải chứa tên gốc "${name}"`).toBeDefined()
      })
    })

    it('chứa sẵn đầy đủ các tên thay thế emoji và CRM UI theo spec', () => {
      const expectedNames = [
        'table',
        'kanban',
        'search',
        'filter',
        'settings',
        'refresh',
        'user',
        'users',
        'phone',
        'mail',
        'calendar',
        'clock',
        'pin',
        'inbox-empty',
        'chart',
        'star',
        'trash',
        'edit',
        'archive',
        'more-vertical',
        'check',
        'x',
        'plus',
        'send',
        'sparkles',
        'tag',
        'note',
        'bell',
        'alert',
        'lock',
        'external-link',
        'message',
        'comment',
        'money',
        'chevron-down',
        'chevron-up',
        'chevron-left',
        'chevron-right',
      ]
      expectedNames.forEach((name) => {
        expect(ICON_MAP[name], `ICON_MAP phải chứa tên "${name}"`).toBeDefined()
      })
    })

    it('tên lạ / không tồn tại → render fallback CircleHelp và cảnh báo console.warn ở DEV mà không crash', () => {
      const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
      const { container } = render(<Icon name="totally-unknown-icon-name-xyz" data-testid="fallback-icon" />)

      const svg = container.querySelector('svg')
      expect(svg).not.toBeNull()
      expect(svg.classList.contains('lucide-circle-help')).toBe(true)

      // Cảnh báo console.warn ở môi trường dev/test
      expect(warnSpy).toHaveBeenCalled()
      const warningText = warnSpy.mock.calls.map((c) => c.join(' ')).join(' ')
      expect(warningText).toContain('totally-unknown-icon-name-xyz')
      expect(warningText).toContain('CircleHelp')
    })

    it('mặc định là icon trang trí: có aria-hidden="true" và không có role="img"', () => {
      const { container } = render(<Icon name="search" />)
      const svg = container.querySelector('svg')
      expect(svg).toHaveAttribute('aria-hidden', 'true')
      expect(svg).not.toHaveAttribute('role', 'img')
    })

    it('khi có prop aria-label hoặc title: có role="img" và không bị ẩn (không có aria-hidden="true")', () => {
      const { container: c1, unmount: u1 } = render(<Icon name="search" aria-label="Tìm kiếm" />)
      const svg1 = c1.querySelector('svg')
      expect(svg1).toHaveAttribute('aria-label', 'Tìm kiếm')
      expect(svg1).toHaveAttribute('role', 'img')
      expect(svg1).not.toHaveAttribute('aria-hidden', 'true')
      u1()

      const { container: c2, unmount: u2 } = render(<Icon name="settings" title="Cài đặt hệ thống" />)
      const svg2 = c2.querySelector('svg')
      expect(svg2).toHaveAttribute('title', 'Cài đặt hệ thống')
      expect(svg2).toHaveAttribute('role', 'img')
      expect(svg2).not.toHaveAttribute('aria-hidden', 'true')
      u2()
    })

    it('nhận các props tùy biến size, color, strokeWidth, className, role và forwarded props', () => {
      const { container } = render(
        <Icon
          name="filter"
          size={24}
          color="#2563eb"
          strokeWidth={1.5}
          className="custom-filter-icon"
          data-testid="custom-icon-test"
        />
      )
      const svg = container.querySelector('svg')
      expect(svg).toHaveAttribute('width', '24')
      expect(svg).toHaveAttribute('height', '24')
      expect(svg).toHaveAttribute('stroke', '#2563eb')
      expect(svg).toHaveAttribute('stroke-width', '1.5')
      expect(svg.classList.contains('custom-filter-icon')).toBe(true)
      expect(svg).toHaveAttribute('data-testid', 'custom-icon-test')
    })
  })

  describe('AC c13c971d (c) — Chỉ Icon.jsx import từ "lucide-react" trong toàn bộ src', () => {
    it('quét tĩnh src/** đảm bảo chỉ shared/components/Icon.jsx import trực tiếp từ "lucide-react"', () => {
      const allFiles = getAllSourceFiles(srcDir)
      expect(allFiles.length).toBeGreaterThan(0)

      const lucideImportPattern = /(?:from\s+['"]lucide-react['"]|import\s*\(['"]lucide-react['"]\))/
      const filesWithLucideImport = []

      for (const file of allFiles) {
        const content = fs.readFileSync(file, 'utf8')
        if (lucideImportPattern.test(content)) {
          const relPath = path.relative(srcDir, file).replace(/\\/g, '/')
          filesWithLucideImport.push(relPath)
        }
      }

      expect(filesWithLucideImport).toEqual(['shared/components/Icon.jsx'])
    })
  })

  describe('AC c13c971d (d) — SourceBadge và PlatformLogo vẫn render logo thương hiệu SVG riêng', () => {
    it('PlatformLogo render logo thương hiệu riêng bằng SVG nội bộ, không phụ thuộc lucide-react', () => {
      const { container: cMessenger, unmount: u1 } = render(<PlatformLogo name="messenger" />)
      const svgMessenger = cMessenger.querySelector('svg')
      expect(svgMessenger).toHaveAttribute('data-testid', 'platform-logo-messenger')
      expect(svgMessenger.querySelector('linearGradient')).not.toBeNull()
      u1()

      const { container: cFacebook, unmount: u2 } = render(<PlatformLogo name="facebook" />)
      const svgFacebook = cFacebook.querySelector('svg')
      expect(svgFacebook).toHaveAttribute('data-testid', 'platform-logo-facebook')
      expect(svgFacebook.querySelector('circle')).toHaveAttribute('fill', '#1877F2')
      u2()

      const { container: cInstagram, unmount: u3 } = render(<PlatformLogo name="instagram" />)
      const svgInstagram = cInstagram.querySelector('svg')
      expect(svgInstagram).toHaveAttribute('data-testid', 'platform-logo-instagram')
      u3()
    })

    it('file PlatformLogo.jsx và SourceBadge.jsx không import từ lucide-react', () => {
      const platformLogoContent = fs.readFileSync(
        path.resolve(srcDir, 'features/inbox/components/PlatformLogo.jsx'),
        'utf8'
      )
      const sourceBadgeContent = fs.readFileSync(
        path.resolve(srcDir, 'features/inbox/components/SourceBadge.jsx'),
        'utf8'
      )

      expect(platformLogoContent).not.toMatch(/lucide-react/)
      expect(sourceBadgeContent).not.toMatch(/lucide-react/)
    })

    it('SourceBadge render logo thương hiệu qua PlatformLogo', () => {
      const item = { channelType: 1 } // Facebook
      const { container } = render(<SourceBadge item={item} />)
      const svg = container.querySelector('svg')
      expect(svg).toHaveAttribute('data-testid', 'platform-logo-facebook')
    })
  })
})
