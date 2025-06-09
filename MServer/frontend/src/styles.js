// src/styles.js

export const nodeStyle = {
    width: 200,
    height: 100,
    borderRadius: '8px',
    boxShadow: '0 4px 8px rgba(0, 0, 0, 0.2)',
    fontFamily: 'Arial, sans-serif',
    color: 'white',
    textAlign: 'center',
    padding: '10px',
    overflow: 'hidden',
    position: 'relative',
};

export const nodeHeaderStyle = {
    marginBottom: 1,
    fontWeight: 'bold',
};

export const nodeIconStyle = {
    width: '50px',
    height: '50px',
    borderRadius: '50%',
    backgroundColor: '#ffffff',
    display: 'flex',
    justifyContent: 'center',
    alignItems: 'center',
    margin: '0 auto',
    boxShadow: '0 4px 8px rgba(0, 0, 0, 0.1)',
};

export const gradientStyles = {
    promptNode: 'linear-gradient(135deg, #6a5acd, #7a5cdb)',
    codeNode: 'linear-gradient(135deg, #ff7043, #ff5722)',
    loadFileNode: 'linear-gradient(135deg, #8bc34a, #4caf50)',
    outputNode: 'linear-gradient(135deg, #ffeb3b, #fbc02d)',
    logNode: 'linear-gradient(135deg, #ff9800, #f57c00)',
};
