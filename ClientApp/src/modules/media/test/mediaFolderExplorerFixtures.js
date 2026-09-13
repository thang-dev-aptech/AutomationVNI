/** Fixtures and API envelopes for MEDIA-04 Folder Explorer tests. */

export const PAGE_A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
export const PAGE_B = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb'

export const CHANNELS = [
  { id: PAGE_A, pageName: 'Page A' },
  { id: PAGE_B, pageName: 'Page B' },
]

export const FOLDER_A_ROOT = {
  id: 'folder-a-root',
  name: 'Campaign A',
  parentFolderId: null,
  socialChannelId: PAGE_A,
  sortOrder: 0,
  childFolderCount: 1,
  directAssetCount: 2,
  assetCount: 2,
  hasChildren: true,
}

export const FOLDER_A_CHILD = {
  id: 'folder-a-child',
  name: 'Child A',
  parentFolderId: FOLDER_A_ROOT.id,
  socialChannelId: PAGE_A,
  sortOrder: 0,
  childFolderCount: 1,
  directAssetCount: 0,
  assetCount: 0,
  hasChildren: true,
}

export const FOLDER_A_GRAND = {
  id: 'folder-a-grand',
  name: 'Grand A',
  parentFolderId: FOLDER_A_CHILD.id,
  socialChannelId: PAGE_A,
  sortOrder: 0,
  childFolderCount: 0,
  directAssetCount: 4,
  assetCount: 4,
  hasChildren: false,
}

export const FOLDER_B_ROOT = {
  id: 'folder-b-root',
  name: 'Campaign B',
  parentFolderId: null,
  socialChannelId: PAGE_B,
  sortOrder: 0,
  childFolderCount: 0,
  directAssetCount: 7,
  assetCount: 7,
  hasChildren: false,
}

export function wrapApiData(data) {
  return { data: { success: true, data } }
}

export function wrapPaged(items, { index = 1, size = 20, total } = {}) {
  return wrapApiData({
    items,
    total: total ?? items.length,
    index,
    size,
  })
}

export function wrapBreadcrumb(ancestors) {
  return wrapApiData({ ancestors })
}

export function deferred() {
  let resolve
  let reject
  const promise = new Promise((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}
