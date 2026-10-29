import { describe, expect, it } from 'vitest'
import { formatDuration, percent, videoSource } from './learning'

describe('videoSource', () => {
  it('embeds YouTube links privately', () => {
    expect(videoSource('https://www.youtube.com/watch?v=abc_123')).toEqual({ kind: 'embed', src: 'https://www.youtube-nocookie.com/embed/abc_123' })
    expect(videoSource('https://youtu.be/xyz')?.src).toBe('https://www.youtube-nocookie.com/embed/xyz')
  })
  it('embeds Google Drive files', () => {
    expect(videoSource('https://drive.google.com/file/d/FILE-1/view?usp=sharing')?.src).toBe('https://drive.google.com/file/d/FILE-1/preview')
  })
  it('plays other https URLs as files and rejects unsafe ones', () => {
    expect(videoSource('https://cdn.example.com/a.mp4')).toEqual({ kind: 'file', src: 'https://cdn.example.com/a.mp4' })
    expect(videoSource('javascript:alert(1)')).toBeNull()
    expect(videoSource('http://insecure.example/a.mp4')).toBeNull()
    expect(videoSource('not a url')).toBeNull()
  })
})

describe('helpers', () => {
  it('computes percent and durations', () => {
    expect(percent(1, 3)).toBe(33)
    expect(percent(0, 0)).toBe(0)
    expect(formatDuration(90)).toBe('2 min')
    expect(formatDuration(3900)).toBe('1 h 5 min')
    expect(formatDuration(null)).toBeNull()
  })
})
