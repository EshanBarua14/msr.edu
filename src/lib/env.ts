const url = import.meta.env.VITE_SUPABASE_URL as string | undefined
const anonKey = import.meta.env.VITE_SUPABASE_ANON_KEY as string | undefined

export const env = {
  supabaseUrl: url,
  supabaseAnonKey: anonKey,
  /** False until Supabase env vars are set; the UI then runs in local preview mode. */
  hasSupabase: Boolean(url && anonKey),
}
