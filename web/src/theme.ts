import { createTheme } from "@mui/material/styles";

// Colors and type taken from the National - US States wireframe.
export const navy = "#2b3152";
const blue = "#2c5aa8";

export const theme = createTheme({
  palette: {
    mode: "light",
    primary: { main: blue },
    secondary: { main: navy },
    success: { main: "#2e9e44" },
    error: { main: "#d32f2f" },
    info: { main: "#1d6fd1" },
    warning: { main: "#b7791f" },
    background: { default: "#f4f4f5", paper: "#ffffff" },
    text: { primary: "#272b3f", secondary: "#6b7185" },
    divider: "#dcdfe6",
  },
  typography: {
    fontFamily: '"Poppins", "Helvetica Neue", Arial, sans-serif',
    fontSize: 13,
    h1: { fontSize: "2.6rem", fontWeight: 500, color: navy, lineHeight: 1.2 },
    h2: { fontSize: "1.3rem", fontWeight: 500, color: navy, marginTop: 32, marginBottom: 10 },
    h3: { fontSize: "1rem", fontWeight: 500, color: navy },
    button: { textTransform: "none", fontWeight: 500 },
  },
  shape: { borderRadius: 4 },
  components: {
    MuiButton: { defaultProps: { disableElevation: true } },
    MuiLink: { defaultProps: { underline: "hover" } },
    MuiTableCell: {
      styleOverrides: {
        root: { verticalAlign: "top", borderColor: "#e6e8ee" },
        head: { fontWeight: 500, fontSize: "1rem", color: "#272b3f" },
      },
    },
    MuiTooltip: {
      defaultProps: { arrow: true },
      styleOverrides: { tooltip: { backgroundColor: "#3a3a3a", fontSize: "0.8rem", padding: "8px 12px" }, arrow: { color: "#3a3a3a" } },
    },
    MuiTab: { styleOverrides: { root: { textTransform: "none", fontWeight: 500 } } },
  },
});
