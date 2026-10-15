import { useState } from "react";
import { NavLink, Route, Routes } from "react-router-dom";
import { AppBar, Badge, Box, Button, Toolbar, Typography } from "@mui/material";
import { isActive, useApi, type JobsData } from "./api";
import { navy } from "./theme";
import { StartRunContext, type StartRunPreset } from "./ui";
import { StartRunDialog } from "./StartRunDialog";
import { Overview } from "./pages/Overview";
import { StatePage } from "./pages/StatePage";
import { ElectionPage } from "./pages/ElectionPage";
import { RunPage } from "./pages/RunPage";
import { CapturePage } from "./pages/CapturePage";
import { Activity } from "./pages/Activity";

const navSx = {
  color: "rgba(255,255,255,0.75)",
  px: 1.5,
  "&.active": { color: "#fff", boxShadow: "inset 0 -2px 0 #fff", borderRadius: 0 },
};

export function App() {
  const [preset, setPreset] = useState<StartRunPreset | null>(null);
  const active = (useApi<JobsData>("/jobs", 2000).data?.jobs ?? []).filter(isActive).length;

  return (
    <StartRunContext.Provider value={setPreset}>
      <AppBar position="static" elevation={0} sx={{ bgcolor: navy }}>
        <Toolbar sx={{ minHeight: { xs: 64, sm: 72 }, gap: 3 }}>
          <Typography sx={{ fontWeight: 600, fontSize: "1.05rem", color: "#fff" }}>Roster console</Typography>
          <Box component="nav" sx={{ display: "flex", alignSelf: "stretch" }}>
            <Button component={NavLink} to="/" end sx={navSx}>States</Button>
            <Button component={NavLink} to="/activity" sx={navSx}>
              <Badge badgeContent={active} color="info" sx={{ "& .MuiBadge-badge": { right: -10 } }}>Activity</Badge>
            </Button>
          </Box>
        </Toolbar>
      </AppBar>
      <Box component="main" sx={{ maxWidth: 1480, mx: "auto", px: { xs: 2, md: 4 }, py: 4, bgcolor: "background.paper", minHeight: "calc(100vh - 72px)" }}>
        <Routes>
          <Route path="/" element={<Overview />} />
          <Route path="/states/:code" element={<StatePage />} />
          <Route path="/states/:code/elections/:key" element={<ElectionPage />} />
          <Route path="/runs/:id" element={<RunPage />} />
          <Route path="/retrieves/:id" element={<CapturePage />} />
          <Route path="/activity" element={<Activity />} />
        </Routes>
      </Box>
      {preset && <StartRunDialog preset={preset} onClose={() => setPreset(null)} />}
    </StartRunContext.Provider>
  );
}
