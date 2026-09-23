import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createBrowserRouter, RouterProvider } from 'react-router'
import './index.css'
import { Home } from './routes/Home.tsx'
import { Model } from './routes/Model.tsx'
import { Plan } from './routes/Plan.tsx'

const rootElement = document.getElementById('root')
if (!rootElement) {
  throw new Error('Missing #root element')
}

const queryClient = new QueryClient()

// docs/ARCHITECTURE.md §7 names five routes; /plan and /model are Phase 4 (docs/ROADMAP.md).
// /bill and /settings remain for a later phase -- the regime advisor backend
// (POST /api/v1/advisor/regime) exists and is tested, but its own screen (the two-bar
// comparison with hatched uncertainty, docs/DESIGN.md §4) is real additional UI work
// deliberately left for when /bill itself is built, not wired into this phase's scope.
const router = createBrowserRouter([
  { path: '/', element: <Home /> },
  { path: '/plan', element: <Plan /> },
  { path: '/model', element: <Model /> },
])

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
)
