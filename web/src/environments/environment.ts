export const environment = {
  production: false,
  /** Dev server (`ng serve`) calls API on Kestrel; Docker UI uses same-origin (empty string). */
  apiBaseUrl: 'http://localhost:5094',
};
