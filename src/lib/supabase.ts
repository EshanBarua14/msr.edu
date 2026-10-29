import { createClient, type SupabaseClient } from '@supabase/supabase-js'
import { env } from './env'

export const supabase: SupabaseClient | null = env.hasSupabase
  ? createClient(env.supabaseUrl!, env.supabaseAnonKey!, {
      auth: { flowType: 'pkce', persistSession: true, autoRefreshToken: true, detectSessionInUrl: true },
    })
  : null
