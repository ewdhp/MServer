import React, { useState } from 'react';
import { Box, Button, MenuItem, Select, TextField, Typography } from '@mui/material';
import axios from 'axios';

const Selector = () => {
    const [selectedModel, setSelectedModel] = useState('openai'); // Default to OpenAI Gemini
    const [localModel, setLocalModel] = useState('');
    const [openAIKey, setOpenAIKey] = useState('');
    const [openAIInput, setOpenAIInput] = useState('');
    const [localModels] = useState(['Local Model 1', 'Local Model 2', 'Local Model 3']); // Example local models
    const [response, setResponse] = useState('');

    const handleModelChange = (event) => {
        setSelectedModel(event.target.value);
        setResponse(''); // Clear the response when switching models
    };

    const handleSubmit = async () => {
        if (selectedModel === 'openai') {
            // Call OpenAI Gemini API
            try {
                const result = await axios.post(
                    'https://api.openai.com/v1/chat/completions',
                    {
                        model: 'gpt-4', // Replace with the correct model name for Gemini
                        messages: [{ role: 'user', content: openAIInput }],
                    },
                    {
                        headers: {
                            Authorization: `Bearer ${openAIKey}`,
                            'Content-Type': 'application/json',
                        },
                    }
                );
                setResponse(result.data.choices[0].message.content);
            } catch (error) {
                console.error('Error calling OpenAI API:', error);
                setResponse('Error calling OpenAI API.');
            }
        } else {
            // Handle local model selection
            setResponse(`You selected the local model: ${localModel}`);
        }
    };

    return (
        <Box
            sx={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                gap: 2,
                padding: 4,
                width: '100%',
                maxWidth: 600,
                margin: '0 auto',
                backgroundColor: '#f9f9f9',
                borderRadius: 2,
                boxShadow: '0 2px 10px rgba(0, 0, 0, 0.1)',
            }}
        >
            <Typography variant="h5" sx={{ marginBottom: 2 }}>
                Model Selector
            </Typography>

            {/* Dropdown to select model */}
            <Select
                value={selectedModel}
                onChange={handleModelChange}
                fullWidth
                variant="outlined"
            >
                <MenuItem value="openai">OpenAI Gemini</MenuItem>
                <MenuItem value="local">Local Model</MenuItem>
            </Select>

            {/* OpenAI Gemini Input */}
            {selectedModel === 'openai' && (
                <>
                    <TextField
                        label="OpenAI API Key"
                        value={openAIKey}
                        onChange={(e) => setOpenAIKey(e.target.value)}
                        fullWidth
                        variant="outlined"
                        type="password"
                    />
                    <TextField
                        label="Input for OpenAI"
                        value={openAIInput}
                        onChange={(e) => setOpenAIInput(e.target.value)}
                        fullWidth
                        variant="outlined"
                    />
                </>
            )}

            {/* Local Model Selection */}
            {selectedModel === 'local' && (
                <Select
                    value={localModel}
                    onChange={(e) => setLocalModel(e.target.value)}
                    fullWidth
                    variant="outlined"
                >
                    {localModels.map((model) => (
                        <MenuItem key={model} value={model}>
                            {model}
                        </MenuItem>
                    ))}
                </Select>
            )}

            {/* Submit Button */}
            <Button
                variant="contained"
                color="primary"
                onClick={handleSubmit}
                fullWidth
                sx={{ marginTop: 2 }}
            >
                Submit
            </Button>

            {/* Response Display */}
            {response && (
                <Box
                    sx={{
                        marginTop: 3,
                        padding: 2,
                        backgroundColor: '#e0e0e0',
                        borderRadius: 1,
                        width: '100%',
                    }}
                >
                    <Typography variant="body1" sx={{ whiteSpace: 'pre-wrap' }}>
                        {response}
                    </Typography>
                </Box>
            )}
        </Box>
    );
};

export default Selector;