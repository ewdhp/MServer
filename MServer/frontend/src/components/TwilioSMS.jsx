import React, { useState } from 'react';
import axios from 'axios';
import { useNavigate } from 'react-router-dom';
import { Box, Typography, TextField, Button, Paper } from '@mui/material';
import { useAuth } from '../context/AuthProvider'; // Import useAuth

const TwilioSMS = () => {
  const [phone, setPhoneNumber] = useState('');
  const [smsCode, setSmsCode] = useState('');
  const [currentStep, setCurrentStep] = useState(0);
  const [errorMessage, setErrorMessage] = useState('');
  const [isResendDisabled, setIsResendDisabled] = useState(false);
  const [resendTimer, setResendTimer] = useState(0);
  const [isLoading, setIsLoading] = useState(false);

  const navigate = useNavigate();
  const authContext = useAuth();

  const handlePhoneNumberChange = (e) => {
    setPhoneNumber(e.target.value);
  };

  const handleSmsCodeChange = (e) => {
    setSmsCode(e.target.value);
  };

  const validatePhoneNumber = (phone) => {
    const phoneRegex = /^\+\d{10,15}$/;
    return phoneRegex.test(phone);
  };

  const validateSmsCode = (code) => {
    const codeRegex = /^\d{6}$/;
    return codeRegex.test(code);
  };

  const handleSendSms = async (e) => {
    e.preventDefault();
    setErrorMessage('');
    setIsLoading(true);

    if (!validatePhoneNumber(`+52${phone}`)) {
      setErrorMessage('El número de teléfono no es válido.');
      setIsLoading(false);
      return;
    }

    try {
      const response = await axios.post('https://localhost:5001/api/testauth/send', {
        Phone: `+52${phone}`,
      });
      if (response.status === 200) {
        setCurrentStep(1);
        startResendCooldown();
      }
    } catch (error) {
      console.error('Error sending SMS:', error);
    } finally {
      setIsLoading(false);
    }
  };

  const handleVerifySms = async (e) => {
    e.preventDefault();
    setErrorMessage('');
    setIsLoading(true);

    if (!validateSmsCode(smsCode)) {
      setErrorMessage('El código SMS debe tener exactamente 6 dígitos.');
      setIsLoading(false);
      return;
    }

    try {
      let token = localStorage.getItem('token');
      if (token) {
        token = `Bearer ${token}`;
      } else {
        console.warn('Token is missing from localStorage');
      }

      const response = await axios.post(
        'https://localhost:5001/api/testauth/verify',
        {
          Phone: `+52${phone}`,
          Code: smsCode,
        },
        {
          headers: {
            Authorization: token || '',
          },
        }
      );
      console.log('Response:', response);
      if (response.status === 200) {
        const { token } = response.data;
        if (token) {
          localStorage.setItem('token', token);
          console.log('Token saved to localStorage:', token);
          authContext.login(token); // Use the authContext instance here
        }

        navigate('/dashboard'); // Redirect to the dashboard
      } else {
        setErrorMessage('El código SMS no es válido. Inténtalo de nuevo.');
      }
    } catch (error) {
      console.error('Error verifying SMS:', error);
      setErrorMessage('Ocurrió un error al verificar el código SMS.');
    } finally {
      setIsLoading(false);
    }
  };

  const startResendCooldown = () => {
    setIsResendDisabled(true);
    let timer = 30;
    setResendTimer(timer);

    const interval = setInterval(() => {
      timer -= 1;
      setResendTimer(timer);

      if (timer <= 0) {
        clearInterval(interval);
        setIsResendDisabled(false);
      }
    }, 1000);
  };

  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        marginTop: '50px',
      }}
    >
      {/* Top Logo and Title */}
      <Box
        sx={{
          display: 'flex',
          flexDirection: 'column',
          alignItems: 'center',
          justifyContent: 'center',
          marginBottom: '20px',
        }}
      >
        {/* SVG Icon */}
        <Box
          component="svg"
          xmlns="http://www.w3.org/2000/svg"
          viewBox="0 0 24 24"
          fill="blue"
          sx={{ width: 48, height: 48, marginBottom: 1 }}
        >
          <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm0 18c-4.41 0-8-3.59-8-8s3.59-8 8-8 8 3.59 8 8-3.59 8-8 8zm-1-13h2v6h-2zm0 8h2v2h-2z" />
        </Box>
        {/* Title */}
        <Typography variant="h6" sx={{ color: 'blue', fontWeight: 'bold', fontSize: '1rem' }}>
          Marketing
        </Typography>
      </Box>

      {/* Login Form */}
      {currentStep === 0 && (
        <Paper
          component="form"
          onSubmit={handleSendSms}
          sx={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            padding: 2,
            borderRadius: 2,
            boxShadow: 3,
            width: '90%',
            maxWidth: 350,
            minWidth: 250,
            margin: '35px auto',
          }}
        >
          <Typography variant="h6" sx={{ marginBottom: 2 }}>
            Teléfono Movil
          </Typography>
          {errorMessage && (
            <Typography variant="body2" color="error" sx={{ marginBottom: 2 }}>
              {errorMessage}
            </Typography>
          )}
          <TextField
            fullWidth
            placeholder="10 dígitos"
            value={phone}
            onChange={handlePhoneNumberChange}
            variant="outlined"
            sx={{
              marginBottom: 2,
              input: {
                textAlign: 'center', // Center the placeholder text
              },
            }}
          />
          <Button
            type="submit"
            variant="contained"
            color="primary"
            fullWidth
            disabled={isLoading}
          >
            {isLoading ? 'Enviando...' : 'Enviar SMS'}
          </Button>
        </Paper>
      )}
      {currentStep === 1 && (
        <Paper
          component="form"
          onSubmit={handleVerifySms}
          sx={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            padding: 2,
            borderRadius: 2,
            boxShadow: 3,
            width: '90%',
            maxWidth: 350,
            minWidth: 250,
            margin: '35px auto',
          }}
        >
          <Typography variant="h6" sx={{ marginBottom: 2 }}>
            Código enviado
          </Typography>
          {errorMessage && (
            <Typography variant="body2" color="error" sx={{ marginBottom: 2 }}>
              {errorMessage}
            </Typography>
          )}
          <TextField
            fullWidth
            placeholder="6 dígitos"
            value={smsCode}
            onChange={handleSmsCodeChange}
            variant="outlined"
            sx={{
              marginBottom: 2,
              input: {
                textAlign: 'center', // Center the placeholder text
              },
            }}
          />
          <Button
            type="submit"
            variant="contained"
            color="primary"
            fullWidth
            disabled={isLoading}
            sx={{ marginBottom: 2 }}
          >
            {isLoading ? 'Verificando...' : 'Verificar SMS'}
          </Button>
          <Button
            type="button"
            variant="outlined"
            color="primary"
            fullWidth
            onClick={startResendCooldown}
            disabled={isResendDisabled}
          >
            {isResendDisabled ? `Reenviar en ${resendTimer}s` : 'Reenviar Código'}
          </Button>
        </Paper>
      )}
    </Box>
  );
};

export default TwilioSMS;