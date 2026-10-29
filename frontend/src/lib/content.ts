export const statusTone = { draft: 'warning', published: 'success', archived: 'neutral' } as const
export type PublishStatus = keyof typeof statusTone

export function slugify(text: string) {
  return text
    .toLowerCase()
    .normalize('NFKD')
    .replace(/[̀-ͯ]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
    .slice(0, 80)
}
