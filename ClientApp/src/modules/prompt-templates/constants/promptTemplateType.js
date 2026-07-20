// Khớp enum backend: Category=0, TextContent=1, Image=2
export const TEMPLATE_TYPE = {
  CATEGORY: 0,
  TEXT: 1,
  IMAGE: 2,
}

export const TEMPLATE_TYPE_LABELS = {
  [TEMPLATE_TYPE.CATEGORY]: 'Danh mục (Text + Ảnh)',
  [TEMPLATE_TYPE.TEXT]: 'Legacy Text',
  [TEMPLATE_TYPE.IMAGE]: 'Legacy Image',
}

export const TEMPLATE_VARIABLES = [
  { name: 'title', desc: 'Tiêu đề / ý tưởng bài viết' },
  { name: 'category', desc: 'Tên danh mục template' },
  { name: 'brand', desc: 'Thương hiệu (PageContext → tên Page kênh)' },
  { name: 'tone', desc: 'Giọng văn (PageContext → mặc định thân thiện)' },
  { name: 'audience', desc: 'Đối tượng (mặc định: khách hàng mục tiêu)' },
  { name: 'objective', desc: 'Mục tiêu (hoặc dùng lại title)' },
  { name: 'cta', desc: 'CTA (PageContext → Tìm hiểu thêm)' },
  { name: 'hashtags', desc: 'Hashtag (PageContext → sinh từ danh mục)' },
  { name: 'caption', desc: '(ảnh) Nội dung bài đã sinh' },
  { name: 'imagePrompt', desc: '(ảnh) Gợi ý prompt ảnh từ AI' },
]
