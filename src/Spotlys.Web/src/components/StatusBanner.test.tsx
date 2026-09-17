import { describe, expect, it } from 'vitest'
import { render, screen } from '@testing-library/react'
import { StatusBanner } from './StatusBanner'
import type { FeedStatus } from '../api/client'

const freshStatus: FeedStatus = {
  jobName: 'IngestDayAheadPrices',
  lastRunStartedAtUtc: new Date().toISOString(),
  lastRunStatus: 'Ok',
  lastRunRows: 24,
  isStale: false,
}

describe('StatusBanner', () => {
  it('renders nothing while loading', () => {
    const { container } = render(<StatusBanner status={undefined} isLoading={true} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing when the feed is fresh', () => {
    const { container } = render(<StatusBanner status={freshStatus} isLoading={false} />)
    expect(container).toBeEmptyDOMElement()
  })

  it('shows a plain-language banner when the feed is stale, before any price is shown', () => {
    render(<StatusBanner status={{ ...freshStatus, isStale: true }} isLoading={false} />)
    expect(screen.getByRole('status')).toBeInTheDocument()
  })

  it('shows the banner when there is no status at all yet', () => {
    render(<StatusBanner status={undefined} isLoading={false} />)
    expect(screen.getByRole('status')).toBeInTheDocument()
  })
})
