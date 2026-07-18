// Khớp enum backend: PromptTemplateType { TextContent = 1, Image = 2 }
export const TEMPLATE_TYPE = {
  TEXT: 1,
  IMAGE: 2,
}

export const TEMPLATE_TYPE_LABELS = {
  [TEMPLATE_TYPE.TEXT]: 'Sinh nội dung (Text)',
  [TEMPLATE_TYPE.IMAGE]: 'Sinh ảnh (Image)',
}

export const TEMPLATE_TYPE_OPTIONS = [
  { value: TEMPLATE_TYPE.TEXT, label: TEMPLATE_TYPE_LABELS[TEMPLATE_TYPE.TEXT] },
  { value: TEMPLATE_TYPE.IMAGE, label: TEMPLATE_TYPE_LABELS[TEMPLATE_TYPE.IMAGE] },
]

// Biến động dùng trong Body (khớp PromptTemplateRenderer.AvailableVariables backend).
export const TEMPLATE_VARIABLES = [
  { name: 'title', desc: 'Tiêu đề bài viết' },
  { name: 'category', desc: 'Tên danh mục' },
  { name: 'brand', desc: 'Tên thương hiệu (PageContext)' },
  { name: 'tone', desc: 'Giọng văn thương hiệu' },
  { name: 'audience', desc: 'Đối tượng mục tiêu' },
  { name: 'objective', desc: 'Mục tiêu/goal của bài' },
  { name: 'cta', desc: 'Call to action mặc định' },
  { name: 'hashtags', desc: 'Hashtag mặc định' },
  { name: 'caption', desc: '(ảnh) Nội dung bài đã sinh' },
  { name: 'imagePrompt', desc: '(ảnh) Gợi ý prompt ảnh từ AI' },
]
