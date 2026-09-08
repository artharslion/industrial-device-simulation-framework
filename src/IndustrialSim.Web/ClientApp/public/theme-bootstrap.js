(() => {
  const key = 'industrial-sim-theme'
  const stored = localStorage.getItem(key)
  const preference = stored === 'light' || stored === 'dark' || stored === 'system' ? stored : 'system'
  const theme = preference === 'system'
    ? (matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
    : preference
  document.documentElement.dataset.theme = theme
  document.documentElement.style.colorScheme = theme
})()
