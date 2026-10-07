import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import MediaBrowserGrid from '../components/MediaBrowserGrid'

const DRIVE = { id: 'drive-root', name: 'Google Drive', socialChannelId: null, childFolderCount: 2, assetCount: 3 }
const PAGE_A = { id: 'page-a-root', name: 'Page A', socialChannelId: 'page-a', pageName: 'Page A' }
const PAGE_B = { id: 'page-b-root', name: 'Page B', socialChannelId: 'page-b', pageName: 'Page B' }

describe('MediaBrowserGrid: Google Drive folder on its own row (user request 2026-10-03)', () => {
  it('root level: puts the Drive folder in its own group, separate from Page folders', () => {
    render(<MediaBrowserGrid folders={[DRIVE, PAGE_A, PAGE_B]} isRootLevel />)

    const driveGroup = screen.getByTestId('media-folder-group-drive')
    const pageGroup = screen.getByTestId('media-folder-group-pages')
    expect(driveGroup.querySelector('.media-folder-group-title')).toHaveTextContent('Google Drive')
    expect(pageGroup.querySelector('.media-folder-group-title')).toHaveTextContent('Page')
    expect(driveGroup.querySelectorAll('.media-folder-card')).toHaveLength(1)
    expect(pageGroup.querySelectorAll('.media-folder-card')).toHaveLength(2)
    expect(within(driveGroup).queryByText('Page A')).not.toBeInTheDocument()
    expect(within(pageGroup).getAllByText('Page A').length).toBeGreaterThan(0)
    expect(within(pageGroup).getAllByText('Page B').length).toBeGreaterThan(0)
    expect(within(pageGroup).queryByText('Google Drive')).not.toBeInTheDocument()
    // Drive row comes before the Page row.
    expect(driveGroup.compareDocumentPosition(pageGroup) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('root level without a Drive folder: one plain grid, no group titles', () => {
    render(<MediaBrowserGrid folders={[PAGE_A, PAGE_B]} isRootLevel />)

    expect(screen.queryByTestId('media-folder-group-drive')).not.toBeInTheDocument()
    expect(screen.queryByTestId('media-folder-group-pages')).not.toBeInTheDocument()
    expect(screen.getAllByText('Page A').length).toBeGreaterThan(0)
  })

  it('inside a folder: page-less child folders stay in the single grid', () => {
    const driveChild = { id: 'drive-child', name: 'Thư mục 3', socialChannelId: null }
    render(<MediaBrowserGrid folders={[driveChild]} isRootLevel={false} />)

    expect(screen.queryByTestId('media-folder-group-drive')).not.toBeInTheDocument()
    expect(screen.getByText('Thư mục 3')).toBeInTheDocument()
  })
})
