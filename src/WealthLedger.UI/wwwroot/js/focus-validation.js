"use strict";

const validationSummary = document.querySelector(
    "[data-validation-summary]");

if (validationSummary instanceof HTMLElement) {
    validationSummary.focus();
}
