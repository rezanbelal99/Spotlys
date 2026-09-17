import type { FeedStatus } from '../api/client'

interface StatusBannerProps {
  status: FeedStatus | undefined
  isLoading: boolean
}

// docs/DEVOPS.md §7: "users are told before they're shown a number" -- shown above the
// ribbon, in plain language, never silently hidden (docs/DATA.md §6: "a dashboard that
// silently shows yesterday's data is a broken dashboard").
export function StatusBanner({ status, isLoading }: StatusBannerProps) {
  if (isLoading) {
    return null
  }

  if (!status || status.isStale) {
    return (
      <div className="status-banner status-banner--stale" role="status">
        Prisdataene er ikke ferske akkurat nå. Vi jobber med å hente nye tall.
      </div>
    )
  }

  return null
}
