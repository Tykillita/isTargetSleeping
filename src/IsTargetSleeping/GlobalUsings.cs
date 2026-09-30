// WPF quita System.IO y System.Net.Http de los using implícitos (chocan con
// System.Windows.Shapes.Path); aquí se vuelven a poner para toda la app.
global using System.IO;
global using System.Net.Http;
global using Path = System.IO.Path;
