// Custom JS for CineDB
console.log("CineDB loaded");

/**
 * Star Rating Component Logic
 * Manages 10-star rating system with half-star precision.
 */
const StarRating = {
    init() {
        document.querySelectorAll('.star-rating-container').forEach(container => {
            // Only initialize if not already initialized
            if (container.dataset.initialized) return;
            
            this.render(container);
            if (container.dataset.readonly !== "true") {
                this.bindEvents(container);
            }
            container.dataset.initialized = "true";
        });
    },

    render(container, hoverValue = null) {
        const rating = hoverValue !== null ? hoverValue : parseFloat(container.dataset.rating || 0);
        const stars = container.querySelectorAll('.star-wrapper');
        const display = container.querySelector('.rating-display-val');

        stars.forEach((star, index) => {
            const starValue = index + 1;
            const icon = star.querySelector('.star-icon');
            
            star.classList.remove('full', 'half', 'empty');
            icon.classList.remove('bi-star-fill', 'bi-star-half', 'bi-star');

            if (rating >= starValue) {
                star.classList.add('full');
                icon.classList.add('bi-star-fill');
            } else if (rating >= starValue - 0.5) {
                star.classList.add('half');
                icon.classList.add('bi-star-half');
            } else {
                star.classList.add('empty');
                icon.classList.add('bi-star');
            }
        });

        if (display) {
            display.textContent = rating.toFixed(1);
        }
    },

    bindEvents(container) {
        const stars = container.querySelectorAll('.star-wrapper');

        stars.forEach((star, index) => {
            const leftPart = star.querySelector('.star-part.left');
            const rightPart = star.querySelector('.star-part.right');

            const handleHover = (val) => this.render(container, val);

            if (leftPart && rightPart) {
                leftPart.addEventListener('mousemove', () => handleHover(index + 0.5));
                rightPart.addEventListener('mousemove', () => handleHover(index + 1));

                leftPart.addEventListener('click', () => this.setRating(container, index + 0.5));
                rightPart.addEventListener('click', () => this.setRating(container, index + 1));
            }
        });

        container.addEventListener('mouseleave', () => {
            this.render(container);
        });
    },

    setRating(container, value) {
        container.dataset.rating = value;
        const input = container.querySelector('.rating-value');
        if (input) {
            input.value = value;
        }
        this.render(container);
        
        // Trigger a change event for the hidden input so other scripts can react
        if (input) {
            input.dispatchEvent(new Event('change', { bubbles: true }));
        }
    }
};

// Initialize on load
document.addEventListener('DOMContentLoaded', () => {
    StarRating.init();
});

// Re-initialize when Bootstrap modals are shown or content is dynamic
document.addEventListener('shown.bs.modal', () => {
    StarRating.init();
});
