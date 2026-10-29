import { describe, expect, it } from 'vitest'
import { slugify } from './content'

describe('slugify', () => {
  it('matches the API slug rule', () => {
    expect(slugify('  Algebra Basics: Part 1! ')).toBe('algebra-basics-part-1')
    expect(slugify('Café déjà vu')).toBe('cafe-deja-vu')
    expect(slugify('---')).toBe('')
  })
})
