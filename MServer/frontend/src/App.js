import React from 'react';
import { BrowserRouter as Router, Routes, Route, Navigate } from 'react-router-dom';
import { CssBaseline } from '@mui/material';
import TwilioSMS from './components/TwilioSMS';
import { AuthProvider, useAuth } from './context/AuthProvider';
import { TerminalProvider } from './context/TerminalProvider';
import Dashboard from './views/Dashboard';

const ProtectedRoute = ({ children }) => {
  const { isAuthenticated } = useAuth();
  return isAuthenticated ? children : <Navigate to="/" />;
};

const RedirectIfAuthenticated = ({ children }) => {
  const { isAuthenticated } = useAuth();
  return isAuthenticated ? <Navigate to="/dashboard" /> : children;
};

const NotFound = () => {
  return (
    <div style={{ textAlign: 'center', marginTop: '50px' }}>
      <h1>404 - Not Found</h1>
      <p>The page you are looking for does not exist.</p>
    </div>
  );
};

const App = () => {
  return (
    <Router>
      <AuthProvider>
        <TerminalProvider>
          <CssBaseline />
          <Routes>
            {/* Entry Page (Login) */}
            <Route
              path="/"
              element={
                <RedirectIfAuthenticated>
                  <TwilioSMS />
                </RedirectIfAuthenticated>
              }
            />

            {/* Protected Routes */}
            <Route
              path="/dashboard"
              element={
                <ProtectedRoute>
                  <Dashboard />                 
                </ProtectedRoute>
              }
            />

            {/* Fallback Route for Not Found */}
            <Route path="*" element={<NotFound />} />
          </Routes>
        </TerminalProvider>
      </AuthProvider>
    </Router>
  );
};

export default App;