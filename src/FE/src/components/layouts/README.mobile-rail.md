# WOR-767 mobile navigation rail

The existing permission-aware `#bottom-nav` remains the single navigation source.

On phone viewports (`max-width: 767px`) AppLayout turns that same DOM into a compact fixed left rail. Tablet keeps the existing bottom navigation and desktop (`min-width: 1120px`) keeps the existing WOR-716 rail.

The phone rail intentionally hides its visual labels while keeping the text in the accessibility tree. Focused editable controls still hide the rail immediately so keyboard/touch interactions cannot be intercepted.
