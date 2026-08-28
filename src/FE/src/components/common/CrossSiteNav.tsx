import './CrossSiteNav.css';

export const CrossSiteNav = () => (
  <div className="cross-site-nav" role="navigation" aria-label="MR Software tjenester">
    <a className="cross-site-nav__brand" href="https://mrsoftware.dk">MR Software</a>
    <div className="cross-site-nav__links">
      <a href="https://mrsoftware.dk">Website</a>
      <a href="https://store.mrsoftware.dk">Store</a>
      <a href="https://store.mrsoftware.dk/pages/demo">Produktdemo</a>
      <span className="is-active" aria-current="page">Ægte demo</span>
      <a className="cross-site-nav__login" href="https://app.mrsoftware.dk/app">Log ind <span aria-hidden="true">↗</span></a>
    </div>
  </div>
);
