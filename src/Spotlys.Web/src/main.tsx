import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createBrowserRouter, RouterProvider } from 'react-router'
import './index.css'
import { Home } from './routes/Home.tsx'
import { Plan } from './routes/Plan.tsx'

const rootElement = document.getElementById('root')
if (!rootElement) {
  throw new Error('Missing #root element')
}

const queryClient = new QueryClient()

// docs/ARCHITECTURE.md §7 names five routes; /plan is the first beyond / (docs/ROADMAP.md
// Phase 4). The others (/bill, /model, /settings) come with their own phases.
const router = createBrowserRouter([
  { path: '/', element: <Home /> },
  { path: '/plan', element: <Plan /> },
])

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
)
